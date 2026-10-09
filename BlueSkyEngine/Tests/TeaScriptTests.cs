using System;
using TeaScript.Frontend;
using TeaScript.Runtime;
using TeaScript.Bridge;

namespace BlueSky.Tests;

public static class TeaScriptTests
{
    public static bool RunAllTests()
    {
        Console.WriteLine("\n[TEST SUITE] Running TeaScript Lexer/Parser/Runtime Tests...");
        bool passed = true;
        
        passed &= TestLexer();
        passed &= TestParserAndInterpreter();
        passed &= TestExecutionLimits();
        passed &= TestHotReloadBridge();
        
        return passed;
    }

    private static bool TestLexer()
    {
        try
        {
            string script = "let speed = 100.0 + 50.0";
            var lexer = new Lexer(script);
            var tokens = lexer.ScanTokens();
            
            bool valid = tokens.Count >= 5;
            Console.WriteLine($"  ✓ Lexer Scanning ({tokens.Count} tokens): {(valid ? "PASSED" : "FAILED")}");
            return valid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Lexer Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestParserAndInterpreter()
    {
        try
        {
            string script = "let x = 10 let y = 20";
            var lexer = new Lexer(script);
            var tokens = lexer.ScanTokens();
            var parser = new Parser(tokens);
            var program = parser.Parse();
            
            var interpreter = new Interpreter();
            interpreter.Execute(program);
            
            Console.WriteLine($"  ✓ Parser & Interpreter Execution: PASSED");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Parser/Interpreter Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestHotReloadBridge()
    {
        try
        {
            var engine = new TeaScriptEngine();
            engine.LoadScriptFromSource("fn update() { let val = 42 }");
            
            Console.WriteLine($"  ✓ Hot-Reload Script Engine Load: PASSED");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Hot-Reload Bridge Exception: {ex.Message}");
            return false;
        }
    }

    private static bool TestExecutionLimits()
    {
        try
        {
            var interpreter = new Interpreter();
            var parser = new Parser(new Lexer("fn loop() { while (true) { } } fn recurse() { recurse() }").ScanTokens());
            interpreter.Execute(parser.Parse());

            bool loopStopped = false;
            try { interpreter.CallFunction("loop"); }
            catch (InvalidOperationException ex) { loopStopped = ex.Message.Contains("step limit"); }

            bool recursionStopped = false;
            try { interpreter.CallFunction("recurse"); }
            catch (InvalidOperationException ex) { recursionStopped = ex.Message.Contains("call depth limit"); }

            bool passed = loopStopped && recursionStopped;
            Console.WriteLine($"  {(passed ? "✓" : "✗")} Script loop and recursion limits: {(passed ? "PASSED" : "FAILED")}");
            return passed;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ Script execution limit test threw: {ex.Message}");
            return false;
        }
    }
}
