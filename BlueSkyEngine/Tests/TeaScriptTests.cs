using System;
using TeaScript.Frontend;
using TeaScript.Runtime;

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
        passed &= TestForLoops();
        passed &= TestCoreBuiltins();
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

    private static bool TestForLoops()
    {
        try
        {
            object? Run(string source, string fn)
            {
                var interpreter = new Interpreter();
                interpreter.Execute(new Parser(new Lexer(source).ScanTokens()).Parse());
                return interpreter.CallFunction(fn);
            }

            // C-style sum 0..9 = 45.
            var sum = Run("fn s() { let t = 0 for (let i = 0; i < 10; i = i + 1) { t = t + i } return t }", "s");
            // break exits early: 0+1+2 = 3.
            var brk = Run("fn b() { let t = 0 for (let i = 0; i < 100; i = i + 1) { if (i == 3) { break } t = t + i } return t }", "b");
            // continue skips odds: 0+2+4+6+8 = 20.
            var cont = Run("fn c() { let t = 0 for (let i = 0; i < 10; i = i + 1) { if (i % 2 == 1) { continue } t = t + i } return t }", "c");
            // while still honors break/continue.
            var w = Run("fn w() { let i = 0 let t = 0 while (true) { i = i + 1 if (i > 10) { break } if (i % 2 == 0) { continue } t = t + i } return t }", "w");
            // Empty clauses: for (;;) with break.
            var inf = Run("fn f() { let i = 0 for (;;) { i = i + 1 if (i == 5) { break } } return i }", "f");
            // Loop var stays scoped: `i` undefined after the loop.
            bool scoped = false;
            try { Run("fn g() { for (let i = 0; i < 2; i = i + 1) { } return i }", "g"); }
            catch (Exception) { scoped = true; }
            // Step limit still trips on infinite for.
            bool limited = false;
            try { Run("fn h() { for (;;) { } }", "h"); }
            catch (InvalidOperationException ex) { limited = ex.Message.Contains("step limit"); }

            bool passed = Equals(sum, 45.0) && Equals(brk, 3.0) && Equals(cont, 20.0)
                && Equals(w, 25.0) && Equals(inf, 5.0) && scoped && limited;
            Console.WriteLine($"  {(passed ? "✓" : "✗")} For loops + break/continue (sum={sum}, break={brk}, cont={cont}, while={w}, scoped={scoped}, limited={limited}): {(passed ? "PASSED" : "FAILED")}");
            return passed;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ For loop test threw: {ex.Message}");
            return false;
        }
    }

    private static bool TestCoreBuiltins()
    {
        try
        {
            var interpreter = new Interpreter();
            interpreter.Execute(new Parser(new Lexer(
                "fn t() { let a = [1, 2, 3] push(a, 4) let n = len(a) let p = pop(a) return [n, p, len(a), sqrt(16.0), abs(0.0 - 3.0), floor(2.7), ceil(2.1), min(3.0, 7.0), max(3.0, 7.0), clamp(9.0, 0.0, 5.0)] }"
            ).ScanTokens()).Parse());
            var result = interpreter.CallFunction("t") as System.Collections.Generic.List<object?>;
            bool passed = result != null && result.Count == 10
                && Equals(result[0], 4.0) && Equals(result[1], 4.0) && Equals(result[2], 3.0)
                && Equals(result[3], 4.0) && Equals(result[4], 3.0) && Equals(result[5], 2.0)
                && Equals(result[6], 3.0) && Equals(result[7], 3.0) && Equals(result[8], 7.0)
                && Equals(result[9], 5.0);
            Console.WriteLine($"  {(passed ? "✓" : "✗")} Core builtins (len/push/pop/math): {(passed ? "PASSED" : "FAILED")}");
            return passed;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ✗ Core builtin test threw: {ex.Message}");
            return false;
        }
    }

    private static bool TestHotReloadBridge()
    {
        try
        {
            var interpreter = new Interpreter();
            var program = new Parser(new Lexer("fn update() { return 42.0 }").ScanTokens()).Parse();
            interpreter.Execute(program);
            bool ok = interpreter.HasFunction("update") && Equals(interpreter.CallFunction("update"), 42.0);
            Console.WriteLine("[TeaScript] Script loaded successfully");
            Console.WriteLine($"  ✓ Hot-Reload Script Engine Load: {(ok ? "PASSED" : "FAILED")}");
            return ok;
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
