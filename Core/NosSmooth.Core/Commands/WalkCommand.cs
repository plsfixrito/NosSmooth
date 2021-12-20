﻿//
//  WalkCommand.cs
//
//  Copyright (c) František Boháček. All rights reserved.
//  Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace NosSmooth.Core.Commands;

/// <summary>
/// Command that moves the player to the specified target position.
/// May be used only in world.
/// </summary>
/// <param name="TargetX">The x coordinate of the target position to move to.</param>
/// <param name="TargetY">The y coordinate of the target position to move to.</param>
public record WalkCommand(int TargetX, int TargetY) : ICommand;