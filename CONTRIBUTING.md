# Contributing to BlueSky Engine

First off, thanks for taking the time to contribute! 🎉

The following is a set of guidelines for contributing to BlueSky Engine. These are mostly guidelines, not rules. Use your best judgment, and feel free to propose changes to this document in a pull request.

## 📋 Table of Contents

- [Code of Conduct](#code-of-conduct)
- [How Can I Contribute?](#how-can-i-contribute)
- [Development Setup](#development-setup)
- [Coding Standards](#coding-standards)
- [Commit Guidelines](#commit-guidelines)
- [Pull Request Process](#pull-request-process)

## 🤝 Code of Conduct

This project and everyone participating in it is governed by respect and professionalism. By participating, you are expected to uphold this standard. Please report unacceptable behavior to the project maintainers.

## 🎯 How Can I Contribute?

### Reporting Bugs

Before creating bug reports, please check existing issues. When creating a bug report, include:

- **Clear title** - Descriptive one-liner
- **Steps to reproduce** - Detailed steps to trigger the bug
- **Expected behavior** - What you expected to happen
- **Actual behavior** - What actually happened
- **Environment** - OS, .NET version, GPU, etc.
- **Screenshots/logs** - If applicable

**Example:**
```markdown
**Title:** Car physics fail on first possession

**Steps:**
1. Launch editor
2. Create new project
3. Enter play mode
4. Press F to possess car
5. Press W

**Expected:** Car should move forward
**Actual:** Car doesn't move on first possession, works on second
**Environment:** macOS 14.0, M1, Metal, .NET 8.0
```

### Suggesting Features

Feature requests are welcome! Please provide:

- **Clear description** - What feature you want
- **Use case** - Why you need it
- **Alternatives** - What workarounds exist
- **Implementation ideas** - How it could work (optional)

### Contributing Code

1. **Fork the repository**
2. **Create a feature branch** - `git checkout -b feature/amazing-feature`
3. **Make your changes** - Follow coding standards below
4. **Test thoroughly** - Add tests for new features
5. **Commit with clear messages** - See commit guidelines
6. **Push to your fork** - `git push origin feature/amazing-feature`
7. **Open a Pull Request**

## 🛠️ Development Setup

### Prerequisites

```bash
# .NET 8.0 SDK
dotnet --version  # Should be 8.0 or higher

# macOS: Xcode CLI tools
xcode-select --install

# Linux: Vulkan drivers
sudo apt install vulkan-tools libvulkan-dev

# Windows: Visual Studio 2022 or Build Tools
```

### Clone & Build

```bash
git clone https://github.com/yourusername/bluesky-engine.git
cd bluesky-engine

# Restore dependencies
dotnet restore

# Build
dotnet build ./BlueSkyEngine/BlueSkyEngine.csproj

# Run tests
dotnet test

# Launch editor
./launch-bluesky.sh
```

### Project Structure

```
BlueSkyEngine/
├── Core/           # Engine core (ECS, memory, threading)
├── Rendering/      # Rendering pipeline
├── RHI/            # Metal, Vulkan, DX11 backends
├── Physics/        # Jolt physics integration
├── Animation/      # Skeletal animation system
├── Audio/          # 3D audio system
├── Editor/         # Editor UI and tools
└── Platform/       # Platform-specific window code
```

## 📝 Coding Standards

### C# Style

- **Use C# 11+ features** - Pattern matching, records, etc.
- **PascalCase** for public members, classes
- **camelCase** for private fields with `_` prefix
- **4 spaces** for indentation (no tabs)
- **Nullable reference types** enabled

**Example:**
```csharp
public class VehiclePhysics
{
    private readonly IPhysicsWorld _physicsWorld;
    private WheelState[] _wheels;

    public void Solve(float deltaTime, float throttleInput, Entity vehicleEntity)
    {
        if (_physicsWorld == null || _wheels == null)
            return;

        // Implementation...
    }
}
```

### Performance Guidelines

- **Avoid allocations in hot paths** - Use `stackalloc`, object pools
- **Cache-friendly data layout** - Struct-of-arrays when possible
- **Minimize virtual calls** - Use sealed classes, static dispatch
- **Profile before optimizing** - Measure, don't guess

**Example:**
```csharp
// ❌ BAD - allocates every frame
public void Update()
{
    var list = new List<Entity>();  // Heap allocation
    foreach (var entity in GetEntities())
        list.Add(entity);
}

// ✅ GOOD - reuse buffer
private List<Entity> _entityBuffer = new(256);

public void Update()
{
    _entityBuffer.Clear();  // No allocation
    foreach (var entity in GetEntities())
        _entityBuffer.Add(entity);
}
```

### Error Handling

- **Check preconditions** early
- **Use exceptions for exceptional cases** - Not control flow
- **Log errors with context** - Include entity IDs, state
- **Fail fast** - Don't hide bugs with try-catch

**Example:**
```csharp
public void AddForce(Entity entity, Vector3 force)
{
    // Precondition checks
    if (!_initialized)
        throw new InvalidOperationException("PhysicsWorld not initialized");
    
    if (!_entityToBody.TryGetValue(entity, out var bodyId))
    {
        Console.WriteLine($"[PhysicsWorld] ⚠️ Entity_{entity.Id} has no physics body");
        return;
    }

    // Apply force
    _bodyInterface.ActivateBody(bodyId);
    _bodyInterface.AddForce(bodyId, force);
}
```

### Comments

- **Don't state the obvious** - Code should be self-documenting
- **Explain WHY, not WHAT** - Reasoning behind decisions
- **Document hacks** - Explain workarounds with TODO/HACK tags
- **API docs** - XML comments for public APIs

**Example:**
```csharp
// ❌ BAD - obvious comment
// Set velocity to zero
velocity = Vector3.Zero;

// ✅ GOOD - explains reasoning
// CRITICAL: Wake sleeping bodies before applying forces!
// Without this, forces accumulate but the body remains asleep.
_bodyInterface.ActivateBody(bodyId);
_bodyInterface.AddForce(bodyId, force);
```

## 📦 Commit Guidelines

### Format

```
<type>(<scope>): <subject>

<body>

<footer>
```

### Types

- **feat**: New feature
- **fix**: Bug fix
- **perf**: Performance improvement
- **refactor**: Code refactoring
- **docs**: Documentation changes
- **test**: Add/update tests
- **chore**: Build, dependencies, tooling

### Examples

```
feat(physics): add vehicle suspension system

Implemented 4-wheel independent suspension with:
- Per-wheel contact detection via raycast
- Spring-damper force calculation
- Wheel position updates based on suspension travel

Closes #42
```

```
fix(rendering): update order causing physics lag

CarController was updating BEFORE Physics.Step(), causing
1-frame delay in force integration. Moved CarController.Update()
to run AFTER physics step.

Fixes #123
```

### Rules

- **Present tense** - "Add feature" not "Added feature"
- **Imperative mood** - "Fix bug" not "Fixes bug"
- **Lowercase subject** - Except proper nouns
- **No period at end** - Of subject line
- **50 char subject** - 72 char body lines

## 🔄 Pull Request Process

### Before Submitting

- [ ] Code builds without errors
- [ ] All tests pass
- [ ] New features have tests
- [ ] Code follows style guidelines
- [ ] Documentation updated (if needed)
- [ ] No debug code/print statements
- [ ] Commit messages are clean

### PR Template

```markdown
## Description
Brief description of changes

## Type of Change
- [ ] Bug fix
- [ ] New feature
- [ ] Performance improvement
- [ ] Breaking change
- [ ] Documentation update

## Testing
How did you test this?

## Screenshots (if applicable)
Before/after images

## Checklist
- [ ] Code builds
- [ ] Tests pass
- [ ] Documentation updated
- [ ] No breaking changes (or documented)
```

### Review Process

1. **Automated checks** - CI must pass
2. **Code review** - At least one maintainer approval
3. **Testing** - Reviewer tests changes locally
4. **Merge** - Squash and merge to main

### After Merge

- PR author can delete their branch
- Changes appear in next release notes
- Feature announced in discussions

## 🎓 Learning Resources

- [.NET Documentation](https://docs.microsoft.com/en-us/dotnet/)
- [Vulkan Tutorial](https://vulkan-tutorial.com/)
- [Metal Best Practices](https://developer.apple.com/metal/)
- [Game Engine Architecture](https://www.gameenginebook.com/)

## ❓ Questions?

- **GitHub Discussions** - For questions and ideas
- **GitHub Issues** - For bug reports and feature requests
- **Code Comments** - Inline questions in PR reviews

## 🙏 Recognition

Contributors will be:
- Listed in release notes
- Mentioned in README acknowledgments
- Given credit in commit history

Thank you for contributing to BlueSky Engine! 🚀
