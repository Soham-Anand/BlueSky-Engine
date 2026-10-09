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

    public TeaScriptBehavior(string scriptPath, Interpreter interpreter, int priority = 0)
    {
        _interpreter = interpreter ?? throw new ArgumentNullException(nameof(interpreter));
        if (!_interpreter.HasFunction("execute"))
            throw new ArgumentException("A TeaScript AI behavior must define fn execute(deltaTime).", nameof(interpreter));
        Priority = priority;
        Name = System.IO.Path.GetFileNameWithoutExtension(scriptPath);
    }

    public override bool CanExecute()
    {
        if (!_interpreter.HasFunction("canExecute"))
            return true;
        return _interpreter.CallFunction("canExecute") is bool canExecute
            ? canExecute
            : throw new InvalidOperationException("AI behavior canExecute() must return a boolean.");
    }

    public override void OnEnter()
    {
        if (_interpreter.HasFunction("onEnter"))
            _interpreter.CallFunction("onEnter");
    }

    public override void Execute(float deltaTime)
    {
        _interpreter.CallFunction("execute", deltaTime);
    }

    public override void OnExit()
    {
        if (_interpreter.HasFunction("onExit"))
            _interpreter.CallFunction("onExit");
    }
}
