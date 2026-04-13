# TeaScript Getting Started Guide

Welcome to TeaScript! This guide will teach you everything you need to know to start scripting gameplay in BlueSky Engine.

## Table of Contents
1. [What is TeaScript?](#what-is-teascript)
2. [Your First Script](#your-first-script)
3. [Language Basics](#language-basics)
4. [Engine Functions](#engine-functions)
5. [Common Patterns](#common-patterns)
6. [Debugging](#debugging)
7. [Best Practices](#best-practices)

---

## What is TeaScript?

TeaScript is a custom scripting language designed specifically for BlueSky Engine. It's:

- **Easy to Learn** - Simple syntax, no complex concepts
- **Game-Focused** - Built-in functions for transforms, math, and time
- **Safe** - Runs in Play mode only, can't break your editor
- **Fast** - Interpreted but optimized for game logic
- **Integrated** - Direct access to ECS entities and components

### Why TeaScript?

Instead of writing C# and recompiling, you can:
- Write gameplay logic in minutes
- Test immediately with Play mode
- Iterate quickly without restarts
- Learn programming in a game context

---

## Your First Script

### Step 1: Create a Script

1. Open BlueSky Engine
2. Create or open a project
3. In the **Content Browser**, right-click → **"New TeaScript"**
4. Name it `HelloWorld.tea`

### Step 2: Write Code

Double-click `HelloWorld.tea` to open the script editor. Type this:

```tea
// My first TeaScript!

fn start() {
    log("Hello from TeaScript!")
    log("This script is running!")
}

fn update() {
    log("Frame update!")
}
```

### Step 3: Attach to Entity

1. Find an entity in the viewport (like the teapot)
2. **Drag** `HelloWorld.tea` from Content Browser onto the entity
3. The entity should show a **green indicator** (script attached)

### Step 4: Run It!

1. Press the **Play ▶️** button
2. Watch the **Output Log** panel
3. You'll see your messages appearing!
4. Press **Stop ⏹️** to return to editor mode

**Congratulations!** You just wrote and ran your first TeaScript! 🎉

---

## Language Basics

### Variables

Variables store data. Use `let` to create them:

```tea
let health = 100
let playerName = "Hero"
let isAlive = true
let speed = 5.5
```

**Types:**
- **Numbers**: `42`, `3.14`, `-10`
- **Strings**: `"Hello"`, `"Player 1"`
- **Booleans**: `true`, `false`
- **Arrays**: `[1, 2, 3, 4, 5]`

### Functions

Functions are reusable blocks of code:

```tea
fn greet(name) {
    log("Hello, " + name + "!")
}

fn add(a, b) {
    return a + b
}

// Call functions
greet("Player")
let sum = add(10, 20)
```

### Conditionals

Make decisions with `if`:

```tea
let health = 50

if (health <= 0) {
    log("Game Over!")
} else if (health < 50) {
    log("Low health!")
} else {
    log("Healthy!")
}
```

### Loops

Repeat code with `while`:

```tea
let i = 0
while (i < 5) {
    log("Count: " + i)
    i = i + 1
}
```

### Operators

**Math:** `+`, `-`, `*`, `/`
```tea
let result = (10 + 5) * 2  // 30
```

**Comparison:** `==`, `!=`, `<`, `>`, `<=`, `>=`
```tea
if (health > 50) {
    log("Healthy")
}
```

**Logical:** `and`, `or`, `not`
```tea
if (hasKey and not doorLocked) {
    log("Door opens!")
}
```

### Arrays

Store multiple values:

```tea
let scores = [100, 200, 300]
let firstScore = scores[0]  // 100
scores[1] = 250  // Change value
```

### Comments

```tea
// This is a single-line comment

let x = 10  // Comments can go at the end of lines

// Use comments to explain your code!
```

---

## Engine Functions

TeaScript provides built-in functions to interact with the game engine.

### Entry Points

These special functions are called automatically by the engine:

#### `start()`
Called **once** when the script first loads (when you press Play).

```tea
fn start() {
    log("Initializing...")
    setPosition(0, 1, 0)
}
```

#### `update()`
Called **every frame** while the game is running.

```tea
fn update() {
    // This runs 60+ times per second!
    let dt = getDeltaTime()
    log("Delta time: " + dt)
}
```

### Transform Functions

Control entity position:

```tea
// Get position
let x = getPositionX()
let y = getPositionY()
let z = getPositionZ()

// Set position
setPositionX(5.0)
setPositionY(2.0)
setPositionZ(-3.0)

// Set all at once
setPosition(5.0, 2.0, -3.0)

// Move by offset
move(1.0, 0, 0)  // Move 1 unit on X axis
```

### Time Functions

```tea
// Get time since last frame (for smooth movement)
let dt = getDeltaTime()

// Example: Move 5 units per second
let speed = 5.0
let x = getPositionX()
setPositionX(x + speed * dt)
```

### Math Functions

```tea
// Trigonometry (angles in radians)
let s = sin(1.57)  // ~1.0
let c = cos(0)     // 1.0

// Useful math
let distance = sqrt(x*x + y*y)
let positive = abs(-10)  // 10
let smaller = min(5, 10)  // 5
let larger = max(5, 10)   // 10
```

### Debug Functions

```tea
// Print to console
log("Debug message")
log("Health: " + health)

// End the game
gameOver()
```

---

## Common Patterns

### Pattern 1: Smooth Movement

Always use `getDeltaTime()` for frame-rate independent movement:

```tea
let speed = 5.0

fn update() {
    let dt = getDeltaTime()
    let x = getPositionX()
    
    // Move right at constant speed
    setPositionX(x + speed * dt)
}
```

### Pattern 2: Circular Motion

```tea
let radius = 5.0
let speed = 2.0
let time = 0

fn update() {
    let dt = getDeltaTime()
    time = time + dt
    
    let x = cos(time * speed) * radius
    let z = sin(time * speed) * radius
    
    setPosition(x, 1, z)
}
```

### Pattern 3: Bouncing

```tea
let velocity = 0
let gravity = -9.8
let bounceStrength = 0.8

fn update() {
    let dt = getDeltaTime()
    let y = getPositionY()
    
    // Apply gravity
    velocity = velocity + gravity * dt
    y = y + velocity * dt
    
    // Bounce when hitting ground
    if (y <= 0) {
        y = 0
        velocity = abs(velocity) * bounceStrength
    }
    
    setPositionY(y)
}
```

### Pattern 4: Oscillation (Bobbing)

```tea
let time = 0
let bobSpeed = 3.0
let bobHeight = 0.5
let baseHeight = 1.0

fn update() {
    let dt = getDeltaTime()
    time = time + dt
    
    let y = baseHeight + sin(time * bobSpeed) * bobHeight
    setPositionY(y)
}
```

### Pattern 5: State Machine

```tea
let state = "idle"
let timer = 0

fn update() {
    let dt = getDeltaTime()
    timer = timer + dt
    
    if (state == "idle") {
        if (timer > 2.0) {
            state = "moving"
            timer = 0
            log("Starting to move!")
        }
    } else if (state == "moving") {
        let x = getPositionX()
        setPositionX(x + 2.0 * dt)
        
        if (timer > 3.0) {
            state = "idle"
            timer = 0
            log("Stopping!")
        }
    }
}
```

---

## Debugging

### Using log()

The `log()` function is your best friend for debugging:

```tea
fn update() {
    let x = getPositionX()
    log("Current X position: " + x)
    
    if (x > 10) {
        log("WARNING: X is too large!")
    }
}
```

### Common Issues

**Script not running?**
- Check if the entity has a green indicator (script attached)
- Make sure you pressed Play ▶️
- Check the Output Log for errors

**Movement too fast/slow?**
- Always use `getDeltaTime()` for movement
- Adjust your speed values

**Script changes not applying?**
- Save the script file (Cmd+S / Ctrl+S)
- Stop and restart Play mode

**Syntax errors?**
- Check for missing parentheses: `fn start() {`
- Check for missing quotes: `"Hello"`
- Check for typos in function names

---

## Best Practices

### 1. Use Meaningful Names

```tea
// ❌ Bad
let x = 5
let t = 0

// ✅ Good
let speed = 5
let elapsedTime = 0
```

### 2. Comment Your Code

```tea
// Calculate circular motion around origin
let x = cos(time) * radius
let z = sin(time) * radius
```

### 3. Keep Functions Small

```tea
// ❌ Bad: One giant update function

// ✅ Good: Split into smaller functions
fn update() {
    handleMovement()
    handleAnimation()
    checkCollisions()
}

fn handleMovement() {
    // Movement code here
}
```

### 4. Use Constants

```tea
// Define constants at the top
let SPEED = 5.0
let JUMP_HEIGHT = 3.0
let GRAVITY = -9.8

fn update() {
    // Use constants
    let x = getPositionX()
    setPositionX(x + SPEED * getDeltaTime())
}
```

### 5. Test Incrementally

Don't write 100 lines and then test. Instead:
1. Write 5-10 lines
2. Press Play and test
3. Fix any issues
4. Add more code
5. Repeat

---

## Next Steps

### Learn More

1. **Read the examples** in `TeaScript/Examples/`:
   - `simple.tea` - Basic syntax
   - `advanced.tea` - Arrays and loops
   - `moving_teapot.tea` - Circular motion
   - `player.tea` - Player controller

2. **Experiment!** Try modifying the examples:
   - Change the speed values
   - Add new behaviors
   - Combine multiple patterns

3. **Build something!** Ideas to try:
   - Make a spinning cube
   - Create a bouncing ball
   - Build a simple enemy AI
   - Make a collectible that moves

### Get Help

- Check the **Output Log** for error messages
- Read the **[TeaScript Language Reference](TeaScript/README.md)**
- Look at the example scripts for inspiration

---

## Quick Reference Card

```tea
// Variables
let name = "value"

// Functions
fn myFunction(param) {
    return param + 1
}

// Conditionals
if (condition) {
    // code
} else {
    // code
}

// Loops
while (condition) {
    // code
}

// Arrays
let arr = [1, 2, 3]
let first = arr[0]

// Entry Points
fn start() { }  // Called once
fn update() { } // Called every frame

// Transform
getPositionX(), getPositionY(), getPositionZ()
setPositionX(x), setPositionY(y), setPositionZ(z)
setPosition(x, y, z)
move(x, y, z)

// Time
getDeltaTime()

// Math
sin(angle), cos(angle)
sqrt(value), abs(value)
min(a, b), max(a, b)

// Debug
log(message)
gameOver()
```

---

**Happy Scripting!** ☕✨

Now go create something amazing with TeaScript!
