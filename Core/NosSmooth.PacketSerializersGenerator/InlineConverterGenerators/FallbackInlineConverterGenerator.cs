//
//  FallbackInlineConverterGenerator.cs
//
//  Copyright (c) František Boháček. All rights reserved.
//  Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.CodeDom.Compiler;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NosSmooth.PacketSerializersGenerator.Errors;

namespace NosSmooth.PacketSerializersGenerator.InlineConverterGenerators;

/// <inheritdoc />
public class FallbackInlineConverterGenerator : IInlineConverterGenerator
{
    /// <inheritdoc />
    public bool ShouldHandle(TypeSyntax? typeSyntax, ITypeSymbol? typeSymbol)
    {
        return true;
    }

    /// <inheritdoc />
    public IError? GenerateSerializerPart
    (
        IndentedTextWriter textWriter,
        string variableName,
        TypeSyntax? typeSyntax,
        ITypeSymbol? typeSymbol
    )
    {
        textWriter.WriteLine
        (
            $"_typeConverterRepository.Serialize<{(typeSyntax?.ToString() ?? typeSymbol!.ToString()).TrimEnd('?')}?>({variableName}, builder);"
        );
        return null;
    }

    /// <inheritdoc />
    public IError? CallDeserialize(IndentedTextWriter textWriter, TypeSyntax? typeSyntax, ITypeSymbol? typeSymbol)
    {
        textWriter.WriteLine
        (
            $"_typeConverterRepository.Deserialize<{(typeSyntax?.ToString() ?? typeSymbol!.ToString()).TrimEnd('?')}?>(stringEnumerator);"
        );
        return null;
    }

    /// <inheritdoc />
    public void GenerateHelperMethods(IndentedTextWriter textWriter)
    {
        // ignore
    }
}