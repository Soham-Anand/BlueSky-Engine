using System;
using System.Collections.Generic;
using TeaScript.Frontend;

namespace TeaScript.Runtime;

/// <summary>
/// Represents a TeaScript function (user-defined).
/// </summary>
public class TeaFunction
{
    public string Name { get; }
    public List<string> Parameters { get; }
    public List<Statement> Body { get; }
    public Environment Closure { get; }
    
    public TeaFunction(string name, List<string> parameters, List<Statement> body, Environment closure)
    {
        Name = name;
        Parameters = parameters;
        Body = body;
        Closure = closure;
    }
}

/// <summary>
/// Represents a native C# function callable from TeaScript.
/// </summary>
public class NativeFunction
{
    public string Name { get; }
    public Func<List<object?>, object?> Implementation { get; }
    
    public NativeFunction(string name, Func<List<object?>, object?> implementation)
    {
        Name = name;
        Implementation = implementation;
    }
}

/// <summary>
/// Special exception for return statements.
/// </summary>
public class ReturnException : Exception
{
    public object? Value { get; }
    
    public ReturnException(object? value)
    {
        Value = value;
    }
}
