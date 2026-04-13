namespace TeaScript.Frontend;

public enum TokenType
{
    // Literals
    Number,
    String,
    Identifier,
    
    // Keywords
    Let,
    Fn,
    If,
    Else,
    Return,
    True,
    False,
    While,
    And,
    Or,
    Not,
    
    // Operators
    Plus,
    Minus,
    Star,
    Slash,
    Assign,
    Equal,
    NotEqual,
    Less,
    Greater,
    LessEqual,
    GreaterEqual,
    
    // Delimiters
    LeftParen,
    RightParen,
    LeftBrace,
    RightBrace,
    LeftBracket,
    RightBracket,
    Comma,
    
    // Special
    Newline,
    EOF
}

public class Token
{
    public TokenType Type { get; }
    public string Lexeme { get; }
    public object? Literal { get; }
    public int Line { get; }
    public int Column { get; }
    
    public Token(TokenType type, string lexeme, object? literal, int line, int column)
    {
        Type = type;
        Lexeme = lexeme;
        Literal = literal;
        Line = line;
        Column = column;
    }
    
    public override string ToString()
    {
        return $"{Type} '{Lexeme}' at {Line}:{Column}";
    }
}
