using System;
using System.Collections.Generic;
using TeaScript.Frontend;

namespace TeaScript.Runtime;

/// <summary>
/// The TeaScript interpreter - executes AST nodes.
/// </summary>
public class Interpreter
{
    private Environment _globalEnvironment;
    private Environment _currentEnvironment;
    
    public Interpreter()
    {
        _globalEnvironment = new Environment();
        _currentEnvironment = _globalEnvironment;
    }
    
    /// <summary>
    /// Register a native function that can be called from TeaScript.
    /// </summary>
    public void RegisterNativeFunction(string name, Func<List<object?>, object?> implementation)
    {
        _globalEnvironment.Define(name, new NativeFunction(name, implementation));
    }
    
    /// <summary>
    /// Execute a program.
    /// </summary>
    public void Execute(Program program)
    {
        foreach (var statement in program.Statements)
        {
            ExecuteStatement(statement);
        }
    }
    
    /// <summary>
    /// Call a TeaScript function by name.
    /// </summary>
    public object? CallFunction(string name, params object?[] args)
    {
        var function = _globalEnvironment.Get(name);
        
        if (function is TeaFunction teaFunc)
        {
            return CallTeaFunction(teaFunc, new List<object?>(args));
        }
        
        if (function is NativeFunction nativeFunc)
        {
            return nativeFunc.Implementation(new List<object?>(args));
        }
        
        throw new Exception($"'{name}' is not a function");
    }
    
    private void ExecuteStatement(Statement statement)
    {
        switch (statement)
        {
            case LetStatement let:
                ExecuteLetStatement(let);
                break;
            case FunctionDeclaration func:
                ExecuteFunctionDeclaration(func);
                break;
            case IfStatement ifStmt:
                ExecuteIfStatement(ifStmt);
                break;
            case WhileStatement whileStmt:
                ExecuteWhileStatement(whileStmt);
                break;
            case ReturnStatement ret:
                ExecuteReturnStatement(ret);
                break;
            case ExpressionStatement expr:
                EvaluateExpression(expr.Expression);
                break;
            default:
                throw new Exception($"Unknown statement type: {statement.GetType().Name}");
        }
    }
    
    private void ExecuteLetStatement(LetStatement statement)
    {
        object? value = EvaluateExpression(statement.Initializer);
        _currentEnvironment.Define(statement.Name, value);
    }
    
    private void ExecuteFunctionDeclaration(FunctionDeclaration statement)
    {
        var function = new TeaFunction(
            statement.Name,
            statement.Parameters,
            statement.Body,
            _currentEnvironment
        );
        _currentEnvironment.Define(statement.Name, function);
    }
    
    private void ExecuteIfStatement(IfStatement statement)
    {
        object? condition = EvaluateExpression(statement.Condition);
        
        if (IsTruthy(condition))
        {
            ExecuteBlock(statement.ThenBranch, new Environment(_currentEnvironment));
        }
        else if (statement.ElseBranch != null)
        {
            ExecuteBlock(statement.ElseBranch, new Environment(_currentEnvironment));
        }
    }
    
    private void ExecuteWhileStatement(WhileStatement statement)
    {
        while (IsTruthy(EvaluateExpression(statement.Condition)))
        {
            ExecuteBlock(statement.Body, new Environment(_currentEnvironment));
        }
    }
    
    private void ExecuteReturnStatement(ReturnStatement statement)
    {
        object? value = statement.Value != null ? EvaluateExpression(statement.Value) : null;
        throw new ReturnException(value);
    }
    
    private void ExecuteBlock(List<Statement> statements, Environment environment)
    {
        Environment previous = _currentEnvironment;
        try
        {
            _currentEnvironment = environment;
            foreach (var statement in statements)
            {
                ExecuteStatement(statement);
            }
        }
        finally
        {
            _currentEnvironment = previous;
        }
    }
    
    private object? EvaluateExpression(Expression expression)
    {
        switch (expression)
        {
            case LiteralExpression literal:
                return literal.Value;
                
            case IdentifierExpression ident:
                return _currentEnvironment.Get(ident.Name);
                
            case BinaryExpression binary:
                return EvaluateBinaryExpression(binary);
                
            case UnaryExpression unary:
                return EvaluateUnaryExpression(unary);
                
            case AssignmentExpression assignment:
                return EvaluateAssignment(assignment);
                
            case CallExpression call:
                return EvaluateCall(call);
                
            case ArrayLiteralExpression array:
                return EvaluateArrayLiteral(array);
                
            case IndexExpression index:
                return EvaluateIndexExpression(index);
                
            default:
                throw new Exception($"Unknown expression type: {expression.GetType().Name}");
        }
    }
    
    private object? EvaluateBinaryExpression(BinaryExpression expression)
    {
        object? left = EvaluateExpression(expression.Left);
        object? right = EvaluateExpression(expression.Right);
        
        switch (expression.Operator)
        {
            case TokenType.Plus:
                if (left is double l1 && right is double r1) return l1 + r1;
                if (left is string || right is string) return $"{left}{right}";
                break;
            case TokenType.Minus:
                return ToNumber(left) - ToNumber(right);
            case TokenType.Star:
                return ToNumber(left) * ToNumber(right);
            case TokenType.Slash:
                return ToNumber(left) / ToNumber(right);
            case TokenType.Equal:
                return IsEqual(left, right);
            case TokenType.NotEqual:
                return !IsEqual(left, right);
            case TokenType.Less:
                return ToNumber(left) < ToNumber(right);
            case TokenType.Greater:
                return ToNumber(left) > ToNumber(right);
            case TokenType.LessEqual:
                return ToNumber(left) <= ToNumber(right);
            case TokenType.GreaterEqual:
                return ToNumber(left) >= ToNumber(right);
            case TokenType.And:
                return IsTruthy(left) && IsTruthy(right);
            case TokenType.Or:
                return IsTruthy(left) || IsTruthy(right);
        }
        
        throw new Exception($"Invalid binary operation: {expression.Operator}");
    }
    
    private object? EvaluateUnaryExpression(UnaryExpression expression)
    {
        object? operand = EvaluateExpression(expression.Operand);
        
        switch (expression.Operator)
        {
            case TokenType.Not:
                return !IsTruthy(operand);
            case TokenType.Minus:
                return -ToNumber(operand);
        }
        
        throw new Exception($"Invalid unary operation: {expression.Operator}");
    }
    
    private object? EvaluateArrayLiteral(ArrayLiteralExpression expression)
    {
        var elements = new List<object?>();
        foreach (var elem in expression.Elements)
        {
            elements.Add(EvaluateExpression(elem));
        }
        return elements;
    }
    
    private object? EvaluateIndexExpression(IndexExpression expression)
    {
        object? array = EvaluateExpression(expression.Array);
        object? index = EvaluateExpression(expression.Index);
        
        if (array is List<object?> list)
        {
            int idx = (int)ToNumber(index);
            if (idx < 0 || idx >= list.Count)
            {
                throw new Exception($"Array index {idx} out of bounds (length: {list.Count})");
            }
            return list[idx];
        }
        
        throw new Exception("Can only index arrays");
    }
    
    private object? EvaluateAssignment(AssignmentExpression expression)
    {
        object? value = EvaluateExpression(expression.Value);
        _currentEnvironment.Set(expression.Name, value);
        return value;
    }
    
    private object? EvaluateCall(CallExpression expression)
    {
        object? callee = _currentEnvironment.Get(expression.Callee);
        
        var arguments = new List<object?>();
        foreach (var arg in expression.Arguments)
        {
            arguments.Add(EvaluateExpression(arg));
        }
        
        if (callee is TeaFunction teaFunc)
        {
            return CallTeaFunction(teaFunc, arguments);
        }
        
        if (callee is NativeFunction nativeFunc)
        {
            return nativeFunc.Implementation(arguments);
        }
        
        throw new Exception($"'{expression.Callee}' is not callable");
    }
    
    private object? CallTeaFunction(TeaFunction function, List<object?> arguments)
    {
        if (arguments.Count != function.Parameters.Count)
        {
            throw new Exception($"Function '{function.Name}' expects {function.Parameters.Count} arguments but got {arguments.Count}");
        }
        
        // Create new environment for function execution
        var functionEnv = new Environment(function.Closure);
        
        // Bind parameters
        for (int i = 0; i < function.Parameters.Count; i++)
        {
            functionEnv.Define(function.Parameters[i], arguments[i]);
        }
        
        // Execute function body
        try
        {
            ExecuteBlock(function.Body, functionEnv);
        }
        catch (ReturnException ret)
        {
            return ret.Value;
        }
        
        return null;
    }
    
    private bool IsTruthy(object? value)
    {
        if (value == null) return false;
        if (value is bool b) return b;
        return true;
    }
    
    private bool IsEqual(object? a, object? b)
    {
        if (a == null && b == null) return true;
        if (a == null) return false;
        return a.Equals(b);
    }
    
    private double ToNumber(object? value)
    {
        if (value is double d) return d;
        if (value is int i) return i;
        if (value is float f) return f;
        throw new Exception($"Cannot convert {value} to number");
    }
}
