using System.Collections.Generic;

namespace TeaScript.Frontend;

// Base node
public abstract class ASTNode { }

// Statements
public abstract class Statement : ASTNode { }

public class LetStatement : Statement
{
    public string Name { get; }
    public Expression Initializer { get; }
    
    public LetStatement(string name, Expression initializer)
    {
        Name = name;
        Initializer = initializer;
    }
}

public class FunctionDeclaration : Statement
{
    public string Name { get; }
    public List<string> Parameters { get; }
    public List<Statement> Body { get; }
    
    public FunctionDeclaration(string name, List<string> parameters, List<Statement> body)
    {
        Name = name;
        Parameters = parameters;
        Body = body;
    }
}

public class IfStatement : Statement
{
    public Expression Condition { get; }
    public List<Statement> ThenBranch { get; }
    public List<Statement>? ElseBranch { get; }
    
    public IfStatement(Expression condition, List<Statement> thenBranch, List<Statement>? elseBranch)
    {
        Condition = condition;
        ThenBranch = thenBranch;
        ElseBranch = elseBranch;
    }
}

public class ReturnStatement : Statement
{
    public Expression? Value { get; }
    
    public ReturnStatement(Expression? value)
    {
        Value = value;
    }
}

public class WhileStatement : Statement
{
    public Expression Condition { get; }
    public List<Statement> Body { get; }
    
    public WhileStatement(Expression condition, List<Statement> body)
    {
        Condition = condition;
        Body = body;
    }
}

public class ExpressionStatement : Statement
{
    public Expression Expression { get; }
    
    public ExpressionStatement(Expression expression)
    {
        Expression = expression;
    }
}

// Expressions
public abstract class Expression : ASTNode { }

public class LiteralExpression : Expression
{
    public object? Value { get; }
    
    public LiteralExpression(object? value)
    {
        Value = value;
    }
}

public class IdentifierExpression : Expression
{
    public string Name { get; }
    
    public IdentifierExpression(string name)
    {
        Name = name;
    }
}

public class BinaryExpression : Expression
{
    public Expression Left { get; }
    public TokenType Operator { get; }
    public Expression Right { get; }
    
    public BinaryExpression(Expression left, TokenType op, Expression right)
    {
        Left = left;
        Operator = op;
        Right = right;
    }
}

public class AssignmentExpression : Expression
{
    public string Name { get; }
    public Expression Value { get; }
    
    public AssignmentExpression(string name, Expression value)
    {
        Name = name;
        Value = value;
    }
}

public class CallExpression : Expression
{
    public string Callee { get; }
    public List<Expression> Arguments { get; }
    
    public CallExpression(string callee, List<Expression> arguments)
    {
        Callee = callee;
        Arguments = arguments;
    }
}

public class UnaryExpression : Expression
{
    public TokenType Operator { get; }
    public Expression Operand { get; }
    
    public UnaryExpression(TokenType op, Expression operand)
    {
        Operator = op;
        Operand = operand;
    }
}

public class ArrayLiteralExpression : Expression
{
    public List<Expression> Elements { get; }
    
    public ArrayLiteralExpression(List<Expression> elements)
    {
        Elements = elements;
    }
}

public class IndexExpression : Expression
{
    public Expression Array { get; }
    public Expression Index { get; }
    
    public IndexExpression(Expression array, Expression index)
    {
        Array = array;
        Index = index;
    }
}

// Program root
public class Program : ASTNode
{
    public List<Statement> Statements { get; }
    
    public Program(List<Statement> statements)
    {
        Statements = statements;
    }
}
