using System;
using System.Collections.Generic;
using System.IO;
using TeaScript.Frontend;
using TeaScript.Runtime;

namespace TeaScript.Bridge;

/// <summary>
/// The main bridge between the C# game engine and TeaScript.
/// This is what the engine uses to load and execute TeaScript code.
/// </summary>
public class TeaScriptEngine
{
    private readonly Interpreter _interpreter;
    private Program? _loadedProgram;
    private bool _isInitialized = false;
    
    public TeaScriptEngine()
    {
        _interpreter = new Interpreter();
    }
    
    /// <summary>
    /// Register a native function that TeaScript can call.
    /// Example: engine.RegisterFunction("movePlayer", (args) => { ... });
    /// </summary>
    public void RegisterFunction(string name, Func<List<object?>, object?> implementation)
    {
        _interpreter.RegisterNativeFunction(name, implementation);
    }
    
    /// <summary>
    /// Load a TeaScript file (.tea).
    /// </summary>
    public void LoadScript(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"TeaScript file not found: {filePath}");
        }
        
        string source = File.ReadAllText(filePath);
        LoadScriptFromSource(source);
    }
    
    /// <summary>
    /// Load TeaScript from a string.
    /// </summary>
    public void LoadScriptFromSource(string source)
    {
        try
        {
            // Lexical analysis
            var lexer = new Lexer(source);
            var tokens = lexer.ScanTokens();
            
            // Parsing
            var parser = new Parser(tokens);
            _loadedProgram = parser.Parse();
            
            // Execute top-level code (variable declarations, function definitions)
            _interpreter.Execute(_loadedProgram);
            
            _isInitialized = false;
            Console.WriteLine("[TeaScript] Script loaded successfully");
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to load TeaScript: {ex.Message}", ex);
        }
    }
    
    /// <summary>
    /// Call the start() function if it exists.
    /// Should be called once when the game starts.
    /// </summary>
    public void CallStart()
    {
        if (_isInitialized)
        {
            Console.WriteLine("[TeaScript] Warning: start() already called");
            return;
        }
        
        try
        {
            _interpreter.CallFunction("start");
            _isInitialized = true;
            Console.WriteLine("[TeaScript] start() executed");
        }
        catch (Exception ex)
        {
            // start() is optional
            if (ex.Message.Contains("Undefined variable"))
            {
                Console.WriteLine("[TeaScript] No start() function defined");
            }
            else
            {
                throw new Exception($"Error in start(): {ex.Message}", ex);
            }
        }
    }
    
    /// <summary>
    /// Call the update() function if it exists.
    /// Should be called every frame.
    /// </summary>
    public void CallUpdate()
    {
        try
        {
            _interpreter.CallFunction("update");
        }
        catch (Exception ex)
        {
            // update() is optional, but if it exists and errors, we should know
            if (!ex.Message.Contains("Undefined variable"))
            {
                throw new Exception($"Error in update(): {ex.Message}", ex);
            }
        }
    }
    
    /// <summary>
    /// Call any TeaScript function by name.
    /// </summary>
    public object? CallFunction(string name, params object?[] args)
    {
        try
        {
            return _interpreter.CallFunction(name, args);
        }
        catch (Exception ex)
        {
            throw new Exception($"Error calling {name}(): {ex.Message}", ex);
        }
    }
    
    /// <summary>
    /// Get the current state (for debugging).
    /// </summary>
    public bool IsInitialized => _isInitialized;
}
