using System;
using System.Collections.Generic;
using System.Linq;

namespace BlueSky.Animation.AnimScript;

/// <summary>
/// AnimScript - A simple animation scripting language for BlueSky Engine.
/// Allows defining animation logic, transitions, and blending without code.
/// 
/// SYNTAX:
///   state StateName {
///     animation "AnimationName"
///     speed 1.0
///     loop true
///     
///     transition TargetState when condition
///   }
/// 
/// EXAMPLE:
///   state Idle {
///     animation "character_idle"
///     loop true
///     
///     transition Walk when speed > 0.1
///     transition Jump when input.jump
///   }
/// </summary>
public class AnimScriptLanguage
{
    public AnimStateMachine StateMachine { get; private set; }
    
    public AnimScriptLanguage()
    {
        StateMachine = new AnimStateMachine();
    }
    
    /// <summary>
    /// Parse AnimScript from text
    /// </summary>
    public bool Parse(string script)
    {
        try
        {
            var lexer = new AnimScriptLexer(script);
            var tokens = lexer.Tokenize();
            
            var parser = new AnimScriptParser(tokens);
            StateMachine = parser.Parse();
            
            Console.WriteLine($"[AnimScript] Parsed successfully: {StateMachine.States.Count} states");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AnimScript] Parse error: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Parse AnimScript from file
    /// </summary>
    public bool ParseFile(string path)
    {
        if (!System.IO.File.Exists(path))
        {
            Console.WriteLine($"[AnimScript] File not found: {path}");
            return false;
        }
        
        string script = System.IO.File.ReadAllText(path);
        return Parse(script);
    }
}

/// <summary>
/// Animation state machine generated from AnimScript
/// </summary>
public class AnimStateMachine
{
    public string Name { get; set; } = "StateMachine";
    public Dictionary<string, AnimState> States { get; set; } = new();
    public string InitialState { get; set; } = "";
    public Dictionary<string, float> Variables { get; set; } = new();
    
    public AnimState? GetState(string name)
    {
        return States.TryGetValue(name, out var state) ? state : null;
    }
}

/// <summary>
/// Animation state with transitions
/// </summary>
public class AnimState
{
    public string Name { get; set; } = "";
    public string AnimationName { get; set; } = "";
    public float Speed { get; set; } = 1.0f;
    public bool Loop { get; set; } = true;
    public float BlendTime { get; set; } = 0.2f;
    
    public List<AnimTransition> Transitions { get; set; } = new();
    
    // Events
    public List<AnimEvent> OnEnter { get; set; } = new();
    public List<AnimEvent> OnExit { get; set; } = new();
    public List<AnimEvent> OnUpdate { get; set; } = new();
}

/// <summary>
/// Transition between states
/// </summary>
public class AnimTransition
{
    public string TargetState { get; set; } = "";
    public AnimCondition Condition { get; set; } = null!;
    public float BlendTime { get; set; } = 0.2f;
    public int Priority { get; set; } = 0; // Higher priority checked first
}

/// <summary>
/// Condition for transitions
/// </summary>
public abstract class AnimCondition
{
    public abstract bool Evaluate(AnimStateMachine machine);
}

/// <summary>
/// Variable comparison condition (e.g., speed > 0.5)
/// </summary>
public class VariableCondition : AnimCondition
{
    public string VariableName { get; set; } = "";
    public ComparisonOp Operator { get; set; }
    public float Value { get; set; }
    
    public override bool Evaluate(AnimStateMachine machine)
    {
        if (!machine.Variables.TryGetValue(VariableName, out float varValue))
            return false;
        
        return Operator switch
        {
            ComparisonOp.Greater => varValue > Value,
            ComparisonOp.GreaterEqual => varValue >= Value,
            ComparisonOp.Less => varValue < Value,
            ComparisonOp.LessEqual => varValue <= Value,
            ComparisonOp.Equal => Math.Abs(varValue - Value) < 0.0001f,
            ComparisonOp.NotEqual => Math.Abs(varValue - Value) >= 0.0001f,
            _ => false
        };
    }
}

/// <summary>
/// Boolean variable condition (e.g., isGrounded)
/// </summary>
public class BoolCondition : AnimCondition
{
    public string VariableName { get; set; } = "";
    public bool ExpectedValue { get; set; } = true;
    
    public override bool Evaluate(AnimStateMachine machine)
    {
        if (!machine.Variables.TryGetValue(VariableName, out float varValue))
            return false;
        
        bool boolValue = varValue > 0.5f;
        return boolValue == ExpectedValue;
    }
}

/// <summary>
/// Compound condition (AND, OR)
/// </summary>
public class CompoundCondition : AnimCondition
{
    public LogicalOp Operator { get; set; }
    public List<AnimCondition> Conditions { get; set; } = new();
    
    public override bool Evaluate(AnimStateMachine machine)
    {
        if (Conditions.Count == 0) return false;
        
        return Operator switch
        {
            LogicalOp.And => Conditions.All(c => c.Evaluate(machine)),
            LogicalOp.Or => Conditions.Any(c => c.Evaluate(machine)),
            _ => false
        };
    }
}

/// <summary>
/// Animation event (callback)
/// </summary>
public class AnimEvent
{
    public string EventName { get; set; } = "";
    public Dictionary<string, object> Parameters { get; set; } = new();
}

public enum ComparisonOp
{
    Greater,
    GreaterEqual,
    Less,
    LessEqual,
    Equal,
    NotEqual
}

public enum LogicalOp
{
    And,
    Or
}

/// <summary>
/// Lexer for AnimScript
/// </summary>
internal class AnimScriptLexer
{
    private readonly string _source;
    private int _position;
    private readonly List<Token> _tokens = new();
    
    public AnimScriptLexer(string source)
    {
        _source = source;
    }
    
    public List<Token> Tokenize()
    {
        while (_position < _source.Length)
        {
            char c = _source[_position];
            
            // Skip whitespace
            if (char.IsWhiteSpace(c))
            {
                _position++;
                continue;
            }
            
            // Skip comments
            if (c == '/' && Peek() == '/')
            {
                while (_position < _source.Length && _source[_position] != '\n')
                    _position++;
                continue;
            }
            
            // String literals
            if (c == '"')
            {
                _tokens.Add(ReadString());
                continue;
            }
            
            // Numbers
            if (char.IsDigit(c) || (c == '-' && char.IsDigit(Peek())))
            {
                _tokens.Add(ReadNumber());
                continue;
            }
            
            // Identifiers and keywords
            if (char.IsLetter(c) || c == '_')
            {
                _tokens.Add(ReadIdentifier());
                continue;
            }
            
            // Operators and punctuation
            switch (c)
            {
                case '{': _tokens.Add(new Token(TokenType.LeftBrace, "{")); _position++; break;
                case '}': _tokens.Add(new Token(TokenType.RightBrace, "}")); _position++; break;
                case '>': 
                    if (Peek() == '=') { _tokens.Add(new Token(TokenType.GreaterEqual, ">=")); _position += 2; }
                    else { _tokens.Add(new Token(TokenType.Greater, ">")); _position++; }
                    break;
                case '<':
                    if (Peek() == '=') { _tokens.Add(new Token(TokenType.LessEqual, "<=")); _position += 2; }
                    else { _tokens.Add(new Token(TokenType.Less, "<")); _position++; }
                    break;
                case '=':
                    if (Peek() == '=') { _tokens.Add(new Token(TokenType.Equal, "==")); _position += 2; }
                    else { _tokens.Add(new Token(TokenType.Assign, "=")); _position++; }
                    break;
                case '!':
                    if (Peek() == '=') { _tokens.Add(new Token(TokenType.NotEqual, "!=")); _position += 2; }
                    else { _tokens.Add(new Token(TokenType.Not, "!")); _position++; }
                    break;
                case '&':
                    if (Peek() == '&') { _tokens.Add(new Token(TokenType.And, "&&")); _position += 2; }
                    else _position++;
                    break;
                case '|':
                    if (Peek() == '|') { _tokens.Add(new Token(TokenType.Or, "||")); _position += 2; }
                    else _position++;
                    break;
                default:
                    _position++;
                    break;
            }
        }
        
        _tokens.Add(new Token(TokenType.EOF, ""));
        return _tokens;
    }
    
    private Token ReadString()
    {
        _position++; // Skip opening quote
        int start = _position;
        
        while (_position < _source.Length && _source[_position] != '"')
            _position++;
        
        string value = _source.Substring(start, _position - start);
        _position++; // Skip closing quote
        
        return new Token(TokenType.String, value);
    }
    
    private Token ReadNumber()
    {
        int start = _position;
        
        if (_source[_position] == '-')
            _position++;
        
        while (_position < _source.Length && (char.IsDigit(_source[_position]) || _source[_position] == '.'))
            _position++;
        
        string value = _source.Substring(start, _position - start);
        return new Token(TokenType.Number, value);
    }
    
    private Token ReadIdentifier()
    {
        int start = _position;
        
        while (_position < _source.Length && (char.IsLetterOrDigit(_source[_position]) || _source[_position] == '_' || _source[_position] == '.'))
            _position++;
        
        string value = _source.Substring(start, _position - start);
        
        // Check for keywords
        var type = value switch
        {
            "state" => TokenType.State,
            "animation" => TokenType.Animation,
            "speed" => TokenType.Speed,
            "loop" => TokenType.Loop,
            "blend" => TokenType.Blend,
            "transition" => TokenType.Transition,
            "when" => TokenType.When,
            "true" => TokenType.True,
            "false" => TokenType.False,
            "on_enter" => TokenType.OnEnter,
            "on_exit" => TokenType.OnExit,
            "on_update" => TokenType.OnUpdate,
            _ => TokenType.Identifier
        };
        
        return new Token(type, value);
    }
    
    private char Peek()
    {
        return _position + 1 < _source.Length ? _source[_position + 1] : '\0';
    }
}

/// <summary>
/// Parser for AnimScript
/// </summary>
internal class AnimScriptParser
{
    private readonly List<Token> _tokens;
    private int _position;
    
    public AnimScriptParser(List<Token> tokens)
    {
        _tokens = tokens;
    }
    
    public AnimStateMachine Parse()
    {
        var machine = new AnimStateMachine();
        
        while (!IsAtEnd())
        {
            if (Match(TokenType.State))
            {
                var state = ParseState();
                machine.States[state.Name] = state;
                
                if (string.IsNullOrEmpty(machine.InitialState))
                    machine.InitialState = state.Name;
            }
            else
            {
                Advance();
            }
        }
        
        return machine;
    }
    
    private AnimState ParseState()
    {
        var state = new AnimState();
        
        // State name
        if (Match(TokenType.Identifier))
        {
            state.Name = Previous().Value;
        }
        
        // State body
        Consume(TokenType.LeftBrace, "Expected '{' after state name");
        
        while (!Check(TokenType.RightBrace) && !IsAtEnd())
        {
            if (Match(TokenType.Animation))
            {
                if (Match(TokenType.String))
                    state.AnimationName = Previous().Value;
            }
            else if (Match(TokenType.Speed))
            {
                if (Match(TokenType.Number))
                    state.Speed = float.Parse(Previous().Value);
            }
            else if (Match(TokenType.Loop))
            {
                if (Match(TokenType.True))
                    state.Loop = true;
                else if (Match(TokenType.False))
                    state.Loop = false;
            }
            else if (Match(TokenType.Blend))
            {
                if (Match(TokenType.Number))
                    state.BlendTime = float.Parse(Previous().Value);
            }
            else if (Match(TokenType.Transition))
            {
                state.Transitions.Add(ParseTransition());
            }
            else
            {
                Advance();
            }
        }
        
        Consume(TokenType.RightBrace, "Expected '}' after state body");
        
        return state;
    }
    
    private AnimTransition ParseTransition()
    {
        var transition = new AnimTransition();
        
        // Target state
        if (Match(TokenType.Identifier))
        {
            transition.TargetState = Previous().Value;
        }
        
        // Condition
        if (Match(TokenType.When))
        {
            transition.Condition = ParseCondition();
        }
        
        return transition;
    }
    
    private AnimCondition ParseCondition()
    {
        // Simple condition: variable op value
        if (Check(TokenType.Identifier))
        {
            string varName = Advance().Value;
            
            if (Match(TokenType.Greater))
            {
                float value = float.Parse(Consume(TokenType.Number, "Expected number").Value);
                return new VariableCondition { VariableName = varName, Operator = ComparisonOp.Greater, Value = value };
            }
            else if (Match(TokenType.GreaterEqual))
            {
                float value = float.Parse(Consume(TokenType.Number, "Expected number").Value);
                return new VariableCondition { VariableName = varName, Operator = ComparisonOp.GreaterEqual, Value = value };
            }
            else if (Match(TokenType.Less))
            {
                float value = float.Parse(Consume(TokenType.Number, "Expected number").Value);
                return new VariableCondition { VariableName = varName, Operator = ComparisonOp.Less, Value = value };
            }
            else if (Match(TokenType.LessEqual))
            {
                float value = float.Parse(Consume(TokenType.Number, "Expected number").Value);
                return new VariableCondition { VariableName = varName, Operator = ComparisonOp.LessEqual, Value = value };
            }
            else if (Match(TokenType.Equal))
            {
                float value = float.Parse(Consume(TokenType.Number, "Expected number").Value);
                return new VariableCondition { VariableName = varName, Operator = ComparisonOp.Equal, Value = value };
            }
            else if (Match(TokenType.NotEqual))
            {
                float value = float.Parse(Consume(TokenType.Number, "Expected number").Value);
                return new VariableCondition { VariableName = varName, Operator = ComparisonOp.NotEqual, Value = value };
            }
            else
            {
                // Boolean variable
                return new BoolCondition { VariableName = varName, ExpectedValue = true };
            }
        }
        
        return new BoolCondition { VariableName = "true", ExpectedValue = true };
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
        if (!IsAtEnd()) _position++;
        return Previous();
    }
    
    private Token Peek() => _tokens[_position];
    private Token Previous() => _tokens[_position - 1];
    private bool IsAtEnd() => Peek().Type == TokenType.EOF;
    
    private Token Consume(TokenType type, string message)
    {
        if (Check(type)) return Advance();
        throw new Exception($"{message} at position {_position}");
    }
}

internal class Token
{
    public TokenType Type { get; set; }
    public string Value { get; set; }
    
    public Token(TokenType type, string value)
    {
        Type = type;
        Value = value;
    }
}

internal enum TokenType
{
    // Keywords
    State, Animation, Speed, Loop, Blend, Transition, When,
    OnEnter, OnExit, OnUpdate,
    
    // Literals
    Identifier, String, Number, True, False,
    
    // Operators
    Greater, GreaterEqual, Less, LessEqual, Equal, NotEqual,
    And, Or, Not, Assign,
    
    // Punctuation
    LeftBrace, RightBrace,
    
    // Special
    EOF
}
