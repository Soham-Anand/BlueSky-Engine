using System;
using System.Collections.Generic;

namespace TeaScript.Frontend;

public class Parser
{
    private readonly List<Token> _tokens;
    private int _current = 0;
    
    public Parser(List<Token> tokens)
    {
        _tokens = tokens;
    }
    
    public Program Parse()
    {
        var statements = new List<Statement>();
        
        while (!IsAtEnd())
        {
            statements.Add(ParseStatement());
        }
        
        return new Program(statements);
    }
    
    private Statement ParseStatement()
    {
        if (Match(TokenType.Let)) return ParseLetStatement();
        if (Match(TokenType.Fn)) return ParseFunctionDeclaration();
        if (Match(TokenType.If)) return ParseIfStatement();
        if (Match(TokenType.While)) return ParseWhileStatement();
        if (Match(TokenType.Return)) return ParseReturnStatement();
        
        return ParseExpressionStatement();
    }
    
    private Statement ParseLetStatement()
    {
        Token name = Consume(TokenType.Identifier, "Expected variable name");
        Consume(TokenType.Assign, "Expected '=' after variable name");
        Expression initializer = ParseExpression();
        
        return new LetStatement(name.Lexeme, initializer);
    }
    
    private Statement ParseFunctionDeclaration()
    {
        Token name = Consume(TokenType.Identifier, "Expected function name");
        Consume(TokenType.LeftParen, "Expected '(' after function name");
        
        var parameters = new List<string>();
        if (!Check(TokenType.RightParen))
        {
            do
            {
                Token param = Consume(TokenType.Identifier, "Expected parameter name");
                parameters.Add(param.Lexeme);
            } while (Match(TokenType.Comma));
        }
        
        Consume(TokenType.RightParen, "Expected ')' after parameters");
        Consume(TokenType.LeftBrace, "Expected '{' before function body");
        
        var body = new List<Statement>();
        while (!Check(TokenType.RightBrace) && !IsAtEnd())
        {
            body.Add(ParseStatement());
        }
        
        Consume(TokenType.RightBrace, "Expected '}' after function body");
        
        return new FunctionDeclaration(name.Lexeme, parameters, body);
    }
    
    private Statement ParseIfStatement()
    {
        Consume(TokenType.LeftParen, "Expected '(' after 'if'");
        Expression condition = ParseExpression();
        Consume(TokenType.RightParen, "Expected ')' after condition");
        Consume(TokenType.LeftBrace, "Expected '{' after condition");
        
        var thenBranch = new List<Statement>();
        while (!Check(TokenType.RightBrace) && !IsAtEnd())
        {
            thenBranch.Add(ParseStatement());
        }
        
        Consume(TokenType.RightBrace, "Expected '}' after then branch");
        
        List<Statement>? elseBranch = null;
        if (Match(TokenType.Else))
        {
            Consume(TokenType.LeftBrace, "Expected '{' after 'else'");
            elseBranch = new List<Statement>();
            while (!Check(TokenType.RightBrace) && !IsAtEnd())
            {
                elseBranch.Add(ParseStatement());
            }
            Consume(TokenType.RightBrace, "Expected '}' after else branch");
        }
        
        return new IfStatement(condition, thenBranch, elseBranch);
    }
    
    private Statement ParseWhileStatement()
    {
        Consume(TokenType.LeftParen, "Expected '(' after 'while'");
        Expression condition = ParseExpression();
        Consume(TokenType.RightParen, "Expected ')' after condition");
        Consume(TokenType.LeftBrace, "Expected '{' before loop body");
        
        var body = new List<Statement>();
        while (!Check(TokenType.RightBrace) && !IsAtEnd())
        {
            body.Add(ParseStatement());
        }
        
        Consume(TokenType.RightBrace, "Expected '}' after loop body");
        
        return new WhileStatement(condition, body);
    }
    
    private Statement ParseReturnStatement()
    {
        Expression? value = null;
        if (!Check(TokenType.RightBrace) && !IsAtEnd())
        {
            value = ParseExpression();
        }
        return new ReturnStatement(value);
    }
    
    private Statement ParseExpressionStatement()
    {
        Expression expr = ParseExpression();
        return new ExpressionStatement(expr);
    }
    
    private Expression ParseExpression()
    {
        return ParseAssignment();
    }
    
    private Expression ParseAssignment()
    {
        Expression expr = ParseLogicalOr();
        
        if (Match(TokenType.Assign))
        {
            if (expr is IdentifierExpression ident)
            {
                Expression value = ParseAssignment();
                return new AssignmentExpression(ident.Name, value);
            }
            throw new Exception("Invalid assignment target");
        }
        
        return expr;
    }
    
    private Expression ParseLogicalOr()
    {
        Expression expr = ParseLogicalAnd();
        
        while (Match(TokenType.Or))
        {
            TokenType op = Previous().Type;
            Expression right = ParseLogicalAnd();
            expr = new BinaryExpression(expr, op, right);
        }
        
        return expr;
    }
    
    private Expression ParseLogicalAnd()
    {
        Expression expr = ParseComparison();
        
        while (Match(TokenType.And))
        {
            TokenType op = Previous().Type;
            Expression right = ParseComparison();
            expr = new BinaryExpression(expr, op, right);
        }
        
        return expr;
    }
    
    private Expression ParseComparison()
    {
        Expression expr = ParseAddition();
        
        while (Match(TokenType.Equal, TokenType.NotEqual, TokenType.Less, 
                     TokenType.Greater, TokenType.LessEqual, TokenType.GreaterEqual))
        {
            TokenType op = Previous().Type;
            Expression right = ParseAddition();
            expr = new BinaryExpression(expr, op, right);
        }
        
        return expr;
    }
    
    private Expression ParseAddition()
    {
        Expression expr = ParseMultiplication();
        
        while (Match(TokenType.Plus, TokenType.Minus))
        {
            TokenType op = Previous().Type;
            Expression right = ParseMultiplication();
            expr = new BinaryExpression(expr, op, right);
        }
        
        return expr;
    }
    
    private Expression ParseMultiplication()
    {
        Expression expr = ParseUnary();
        
        while (Match(TokenType.Star, TokenType.Slash))
        {
            TokenType op = Previous().Type;
            Expression right = ParseUnary();
            expr = new BinaryExpression(expr, op, right);
        }
        
        return expr;
    }
    
    private Expression ParseUnary()
    {
        if (Match(TokenType.Not, TokenType.Minus))
        {
            TokenType op = Previous().Type;
            Expression operand = ParseUnary();
            return new UnaryExpression(op, operand);
        }
        
        return ParsePostfix();
    }
    
    private Expression ParsePostfix()
    {
        Expression expr = ParsePrimary();
        
        while (true)
        {
            if (Match(TokenType.LeftBracket))
            {
                Expression index = ParseExpression();
                Consume(TokenType.RightBracket, "Expected ']' after array index");
                expr = new IndexExpression(expr, index);
            }
            else
            {
                break;
            }
        }
        
        return expr;
    }
    
    private Expression ParsePrimary()
    {
        if (Match(TokenType.True)) return new LiteralExpression(true);
        if (Match(TokenType.False)) return new LiteralExpression(false);
        
        if (Match(TokenType.Number, TokenType.String))
        {
            return new LiteralExpression(Previous().Literal);
        }
        
        // Array literal
        if (Match(TokenType.LeftBracket))
        {
            var elements = new List<Expression>();
            if (!Check(TokenType.RightBracket))
            {
                do
                {
                    elements.Add(ParseExpression());
                } while (Match(TokenType.Comma));
            }
            Consume(TokenType.RightBracket, "Expected ']' after array elements");
            return new ArrayLiteralExpression(elements);
        }
        
        if (Match(TokenType.Identifier))
        {
            string name = Previous().Lexeme;
            
            // Function call
            if (Match(TokenType.LeftParen))
            {
                var arguments = new List<Expression>();
                if (!Check(TokenType.RightParen))
                {
                    do
                    {
                        arguments.Add(ParseExpression());
                    } while (Match(TokenType.Comma));
                }
                Consume(TokenType.RightParen, "Expected ')' after arguments");
                return new CallExpression(name, arguments);
            }
            
            return new IdentifierExpression(name);
        }
        
        if (Match(TokenType.LeftParen))
        {
            Expression expr = ParseExpression();
            Consume(TokenType.RightParen, "Expected ')' after expression");
            return expr;
        }
        
        throw new Exception($"Unexpected token: {Peek()}");
    }
    
    private bool Match(params TokenType[] types)
    {
        foreach (var type in types)
        {
            if (Check(type))
            {
                Advance();
                return true;
            }
        }
        return false;
    }
    
    private bool Check(TokenType type)
    {
        if (IsAtEnd()) return false;
        return Peek().Type == type;
    }
    
    private Token Advance()
    {
        if (!IsAtEnd()) _current++;
        return Previous();
    }
    
    private Token Peek() => _tokens[_current];
    private Token Previous() => _tokens[_current - 1];
    private bool IsAtEnd() => Peek().Type == TokenType.EOF;
    
    private Token Consume(TokenType type, string message)
    {
        if (Check(type)) return Advance();
        throw new Exception($"{message} at {Peek()}");
    }
}
