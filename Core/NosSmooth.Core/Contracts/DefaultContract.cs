//
//  DefaultContract.cs
//
//  Copyright (c) František Boháček. All rights reserved.
//  Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Remora.Results;

namespace NosSmooth.Core.Contracts;

/// <summary>
/// A generic implementation of contract
/// supporting any data.
/// </summary>
/// <typeparam name="TData">The data type.</typeparam>
/// <typeparam name="TState">The states.</typeparam>
/// <typeparam name="TError">The errors that may be returned.</typeparam>
public class DefaultContract<TData, TState, TError> : IContract<TData, TState>
    where TState : struct, IComparable
    where TData : notnull
{
    /// <summary>
    /// An action to execute when a state is reached.
    /// </summary>
    /// <param name="data">The data that led to the state.</param>
    /// <param name="ct">The cancellation token used for cancelling the operation.</param>
    /// <returns>The result that may or may not have succeeded.</returns>
    public delegate Task<Result<(TError? Error, TState? NextState)>> StateActionAsync(object? data, CancellationToken ct);

    /// <summary>
    /// An action to execute when a state that may fill the data is reached.
    /// Returns the data to fill.
    /// </summary>
    /// <param name="data">The data that led to the state.</param>
    /// <param name="ct">The cancellation token used for cancelling the operation.</param>
    /// <returns>The result that may or may not have succeeded.</returns>
    public delegate Task<Result<TData>> FillDataAsync(object data, CancellationToken ct);

    private readonly IDictionary<TState, StateActionAsync> _actions;
    private readonly Contractor _contractor;
    private readonly TState _defaultState;

    private readonly TState _fillAtState;
    private readonly FillDataAsync _fillData;

    private readonly TimeSpan? _timeout;

    private TError? _error;
    private Result? _resultError;

    private TState? _waitingFor;
    private bool _unregisterAtWaitingFor;
    private CancellationTokenSource? _waitCancellationSource;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultContract{TData, TState, TError}"/> class.
    /// </summary>
    /// <param name="contractor">The contractor.</param>
    /// <param name="defaultState">The default state.</param>
    /// <param name="fillAtState">The state to fill data at.</param>
    /// <param name="fillData">The function to fill the data.</param>
    /// <param name="actions">The actions to execute at each state.</param>
    /// <param name="timeout">The timeout.</param>
    public DefaultContract
    (
        Contractor contractor,
        TState defaultState,
        TState fillAtState,
        FillDataAsync fillData,
        IDictionary<TState, StateActionAsync> actions,
        TimeSpan? timeout
    )
    {
        _timeout = timeout;

        _defaultState = defaultState;
        _contractor = contractor;
        CurrentState = defaultState;

        _actions = actions;
        _fillData = fillData;
        _fillAtState = fillAtState;
    }

    /// <inheritdoc />
    public TState CurrentState { get; private set; }

    /// <inheritdoc />
    public TData? Data { get; private set; }

    /// <inheritdoc />
    public bool IsRegistered { get; private set; }

    /// <inheritdoc />
    public void Register()
    {
        if (!IsRegistered)
        {
            _contractor.Register(this);
            IsRegistered = true;
        }
    }

    /// <inheritdoc />
    public void Unregister()
    {
        if (IsRegistered)
        {
            _contractor.Unregister(this);
            IsRegistered = false;
        }
    }

    /// <inheritdoc />
    public async Task<Result<ContractUpdateResponse>> Update<TAny>(TAny data, CancellationToken ct = default)
    {
        if (!_actions.ContainsKey(CurrentState))
        {
            throw new Exception(); // ?
        }

        var result = await _actions[CurrentState](data, ct);
        if (!result.IsDefined(out var resultData))
        {
            _resultError = Result.FromError(result);
            _waitCancellationSource?.Cancel();
            return Result<ContractUpdateResponse>.FromError(result);
        }

        if (resultData.Error is not null)
        {
            _error = resultData.Error;
            _waitCancellationSource?.Cancel();
        }

        if (resultData.NextState is null)
        {
            return ContractUpdateResponse.NotInterested;
        }

        CurrentState = resultData.NextState.Value;
        if (_fillAtState.CompareTo(CurrentState) == 0)
        {
            var filledResult = await _fillData(data!, ct);

            if (!filledResult.IsDefined(out var filled))
            {
                _resultError = Result.FromError(filledResult);
                _waitCancellationSource?.Cancel();
                return Result<ContractUpdateResponse>.FromError(filledResult);
            }

            Data = filled;
        }

        if (_waitingFor is not null && _waitingFor.Value.CompareTo(CurrentState) == 0)
        {
            IsRegistered = false; // avoid deadlock. The cancellation will trigger unregister,

                                  // but we are inside of the lock now.
            _waitCancellationSource?.Cancel();

            if (_unregisterAtWaitingFor)
            {
                return ContractUpdateResponse.InterestedAndUnregister;
            }
        }

        // TODO: timeouts!
        return ContractUpdateResponse.Interested;
    }

    /// <inheritdoc />
    public async Task<Result> OnlyExecuteAsync(CancellationToken ct = default)
    {
        if (_actions.ContainsKey(_defaultState))
        {
            var result = await _actions[_defaultState](default, ct);
            if (!result.IsSuccess)
            {
                return Result.FromError(result);
            }
        }

        return Result.FromSuccess();
    }

    /// <inheritdoc />
    public async Task<Result<TData>> WaitForAsync
        (TState state, bool unregisterAfter = true, CancellationToken ct = default)
    {
        if (_fillAtState.CompareTo(state) > 0)
        {
            throw new InvalidOperationException
            (
                $"The requested state {state} does not guarantee data filled. The state that fills data is {_defaultState}"
            );
        }

        _waitingFor = state;
        _unregisterAtWaitingFor = unregisterAfter;
        _waitCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(ct);

        if (_timeout is not null)
        {
            _waitCancellationSource.CancelAfter(_timeout.Value);
        }

        Register();

        if (CurrentState.CompareTo(_defaultState) == 0)
        {
            var result = await OnlyExecuteAsync(ct);
            if (!result.IsSuccess)
            {
                Unregister();
                return Result<TData>.FromError(result);
            }
        }

        try
        {
            await Task.Delay(-1, _waitCancellationSource.Token);
        }
        catch
        {
            // ignored
        }
        finally
        {
            if (unregisterAfter)
            {
                Unregister();
            }
        }

        if (ct.IsCancellationRequested)
        {
            throw new TaskCanceledException();
        }

        if (_resultError is not null)
        {
            return Result<TData>.FromError(_resultError.Value);
        }

        if (_error is not null)
        {
            return new ContractError<TError>(_error);
        }

        if (Data is null)
        {
            throw new Exception("Data was null, but shouldn't have. There is an error in DefaultContract.");
        }

        return Data;
    }
}