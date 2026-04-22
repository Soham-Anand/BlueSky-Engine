using System;
using TeaScript.Runtime;

namespace BlueSky.AI.Overthinking;

/// <summary>
/// AI Behavior driven by TeaScript
/// Allows designers to write AI logic in TeaScript without C# compilation
/// </summary>
public class TeaScriptBehavior : AIBehavior
{
    private readonly Interpreter _interpreter;
    private readonly string _scriptPath;

    public TeaScriptBehavior(string scriptPath, Interpreter interpreter, int priority = 0)
    {
        _scriptPath = scriptPath;
        _interpreter = interpreter;
        Priority = priority;
        Name = System.IO.Path.GetFileNameWithoutExtension(scriptPath);
    }

    public override bool CanExecute()
    {
        // Call TeaScript function: canExecute()
        try
        {
            var result = _interpreter.CallFunction("canExecute");
            return result is bool b && b;
        }
        catch
        {
            return true; // Default to always executable if function doesn't exist
        }
    }

    public override void OnEnter()
    {
        // Call TeaScript function: onEnter()
        try
        {
            _interpreter.CallFunction("onEnter");
        }
        catch
        {
            // Function doesn't exist, ignore
        }
    }

    public override void Execute(float deltaTime)
    {
        // Call TeaScript function: execute(deltaTime)
        try
        {
            _interpreter.CallFunction("execute", deltaTime);
        }
        catch
        {
            // Function doesn't exist, ignore
        }
    }

    public override void OnExit()
    {
        // Call TeaScript function: onExit()
        try
        {
            _interpreter.CallFunction("onExit");
        }
        catch
        {
            // Function doesn't exist, ignore
        }
    }
}
