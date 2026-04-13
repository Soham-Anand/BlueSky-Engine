using System;
using System.Collections.Generic;
using System.Text;

namespace TeaScript.Frontend;

public class Lexer
{
    private readonly string _source;
    private readonly List<Token> _tokens = new();
    private int _start = 0;
    private int _current = 0;
    private int _line = 1;
    private int _column = 1;
    
    private static readonly Dictionary<string, TokenType> Keywords = new()
    {
        { "let", TokenType.Let },
        { "fn", TokenType.Fn },
        { "if", TokenType.If },
        { "else", TokenType.Else },
        { "return", TokenType.Return },
        { "true", TokenType.True },
        { "false", TokenType.False },
        { "while", TokenType.While },
        { "and", TokenType.And },
        { "or", TokenType.Or },
        { "not", TokenType.Not }
    };
    
    public Lexer(string source)
    {
        _source = source;
    }
    
    public List<Token> ScanTokens()
    {
        while (!IsAtEnd())
        {
            _start = _current;
            ScanToken();
        }
        
        _tokens.Add(new Token(TokenType.EOF, "", null, _line, _column));
        return _tokens;
    }
    
    private void ScanToken()
    {
        char c = Advance();
        
        switch (c)
        {
            case '(': AddToken(TokenType.LeftParen); break;
            case ')': AddToken(TokenType.RightParen); break;
            case '{': AddToken(TokenType.LeftBrace); break;
            case '}': AddToken(TokenType.RightBrace); break;
            case '[': AddToken(TokenType.LeftBracket); break;
            case ']': AddToken(TokenType.RightBracket); break;
            case ',': AddToken(TokenType.Comma); break;
            case '+': AddToken(TokenType.Plus); break;
            case '-': AddToken(TokenType.Minus); break;
            case '*': AddToken(TokenType.Star); break;
            case '/':
                if (Match('/'))
                {
                    // Comment - skip to end of line
                    while (Peek() != '\n' && !IsAtEnd()) Advance();
                }
                else
                {
                    AddToken(TokenType.Slash);
                }
                break;
            case '=':
                AddToken(Match('=') ? TokenType.Equal : TokenType.Assign);
                break;
            case '!':
                if (Match('='))
                    AddToken(TokenType.NotEqual);
                break;
            case '<':
                AddToken(Match('=') ? TokenType.LessEqual : TokenType.Less);
                break;
            case '>':
                AddToken(Match('=') ? TokenType.GreaterEqual : TokenType.Greater);
                break;
            case ' ':
            case '\r':
            case '\t':
                // Ignore whitespace
                break;
            case '\n':
                _line++;
                _column = 1;
                break;
            case '"':
                ScanString();
                break;
            default:
                if (IsDigit(c))
                {
                    ScanNumber();
                }
                else if (IsAlpha(c))
                {
                    ScanIdentifier();
                }
                else
                {
                    throw new Exception($"Unexpected character '{c}' at {_line}:{_column}");
                }
                break;
        }
    }
    
    private void ScanString()
    {
        var value = new StringBuilder();
        
        while (Peek() != '"' && !IsAtEnd())
        {
            if (Peek() == '\n')
            {
                _line++;
                _column = 1;
            }
            value.Append(Advance());
        }
        
        if (IsAtEnd())
        {
            throw new Exception($"Unterminated string at {_line}:{_column}");
        }
        
        // Closing "
        Advance();
        
        AddToken(TokenType.String, value.ToString());
    }
    
    private void ScanNumber()
    {
        while (IsDigit(Peek())) Advance();
        
        // Look for decimal part
        if (Peek() == '.' && IsDigit(PeekNext()))
        {
            Advance(); // Consume '.'
            while (IsDigit(Peek())) Advance();
        }
        
        string text = _source.Substring(_start, _current - _start);
        AddToken(TokenType.Number, double.Parse(text));
    }
    
    private void ScanIdentifier()
    {
        while (IsAlphaNumeric(Peek())) Advance();
        
        string text = _source.Substring(_start, _current - _start);
        TokenType type = Keywords.ContainsKey(text) ? Keywords[text] : TokenType.Identifier;
        
        // Handle boolean literals
        object? literal = null;
        if (type == TokenType.True) literal = true;
        if (type == TokenType.False) literal = false;
        
        AddToken(type, literal);
    }
    
    private bool Match(char expected)
    {
        if (IsAtEnd()) return false;
        if (_source[_current] != expected) return false;
        
        _current++;
        _column++;
        return true;
    }
    
    private char Peek()
    {
        if (IsAtEnd()) return '\0';
        return _source[_current];
    }
    
    private char PeekNext()
    {
        if (_current + 1 >= _source.Length) return '\0';
        return _source[_current + 1];
    }
    
    private char Advance()
    {
        _column++;
        return _source[_current++];
    }
    
    private void AddToken(TokenType type, object? literal = null)
    {
        string text = _source.Substring(_start, _current - _start);
        _tokens.Add(new Token(type, text, literal, _line, _column));
    }
    
    private bool IsAtEnd() => _current >= _source.Length;
    private bool IsDigit(char c) => c >= '0' && c <= '9';
    private bool IsAlpha(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
    private bool IsAlphaNumeric(char c) => IsAlpha(c) || IsDigit(c);
}
