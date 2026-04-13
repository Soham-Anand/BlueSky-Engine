# TeaScript

A lightweight, easy-to-learn scripting language for BlueSky Engine gameplay programming.

## What is TeaScript?

TeaScript is designed for game developers who want to write gameplay logic without the complexity of C#. It's perfect for:
- Entity behaviors and AI
- Game mechanics and rules
- Interactive objects
- Player controllers
- Simple animations

## Quick Example

```tea
// Simple player movement script
var speed = 5.0

function start() {
    log("Player controller initialized!")
}

function update() {
    var dt = getDeltaTime()
    var x = getPositionX()
    
    // Move right
    setPositionX(x + speed * dt)
}
```

## Language Features

### Variables
```tea
var health = 100
var playerName = "Hero"
var isAlive = true
```

### Functions
```tea
function takeDamage(amount) {
    health = health - amount
    log("Took damage: " + amount)
    return health
}
```

### Conditionals
```tea
if (health <= 0) {
    log("Game Over!")
} else {
    log("Still alive!")
}
```

### Loops
```tea
var i = 0
while (i < 10) {
    log("Count: " + i)
    i = i + 1
}
```

### Arrays
```tea
var positions = [0, 5, 10, 15]
var firstPos = positions[0]
```

### Logical Operators
```tea
if (health > 0 and not isDead) {
    // Player is alive
}

if (hasKey or hasCard) {
    // Can open door
}
```

### Comments
```tea
// This is a single-line comment
var x = 10  // Comments can go at the end of lines
```

## Engine Functions

### Transform
- `getPositionX()`, `getPositionY()`, `getPositionZ()` - Get position
- `setPositionX(x)`, `setPositionY(y)`, `setPositionZ(z)` - Set position
- `setPosition(x, y, z)` - Set all coordinates at once
- `move(x, y, z)` - Move by offset

### Math
- `sin(angle)`, `cos(angle)` - Trigonometry
- `sqrt(value)` - Square root
- `abs(value)` - Absolute value
- `min(a, b)`, `max(a, b)` - Min/max values

### Time
- `getDeltaTime()` - Time since last frame (for smooth movement)

### Debug
- `log(message)` - Print to console

### Input (Placeholders)
- `getKey(keyCode)` - Check if key is pressed
- `getMouseButton(button)` - Check if mouse button is pressed

## Entry Points

TeaScript has two special functions that the engine calls automatically:

### `start()`
Called once when the script is first loaded. Use this for initialization.

```tea
function start() {
    log("Script initialized!")
    setPosition(0, 0, 0)
}
```

### `update()`
Called every frame while the game is running. Use this for continuous logic.

```tea
function update() {
    var dt = getDeltaTime()
    // Your game logic here
}
```

## Example Scripts

### Simple Movement
```tea
// Move object in a circle
var time = 0
var radius = 5

function update() {
    time = time + getDeltaTime()
    
    var x = cos(time) * radius
    var z = sin(time) * radius
    
    setPosition(x, 0, z)
}
```

### Advanced Behavior
```tea
// Bouncing object with state
var velocity = 0
var gravity = -9.8
var bounceHeight = 3

function start() {
    setPosition(0, bounceHeight, 0)
}

function update() {
    var dt = getDeltaTime()
    var y = getPositionY()
    
    // Apply gravity
    velocity = velocity + gravity * dt
    y = y + velocity * dt
    
    // Bounce when hitting ground
    if (y <= 0) {
        y = 0
        velocity = abs(velocity) * 0.8  // Lose some energy
    }
    
    setPositionY(y)
}
```

## Using TeaScript in BlueSky Engine

1. **Create a Script**: Right-click in Content Browser → "New TeaScript"
2. **Edit the Script**: Double-click the .tea file to open the editor
3. **Attach to Entity**: Drag the script onto an entity in the viewport
4. **Press Play**: Your script runs automatically!
5. **Press Stop**: Scene returns to editor state (changes don't persist)

## Tips

- Use `log()` frequently to debug your scripts
- `getDeltaTime()` makes movement frame-rate independent
- Scripts only run when Play mode is active
- Changes during Play mode don't affect your editor scene
- Keep scripts simple - complex logic should be in C#

## File Locations

- **Scripts**: `TeaScript/Examples/` - Example scripts to learn from
- **Your Scripts**: Create in your project's Assets folder
- **Engine Integration**: `BlueSkyEngine/Core/Scripting/TeaScriptSystem.cs`

## What's Next?

Check out the example scripts in `TeaScript/Examples/`:
- `simple.tea` - Basic variable and function usage
- `player.tea` - Simple player controller
- `advanced.tea` - Complex behaviors with arrays and loops
- `moving_teapot.tea` - Circular motion with math functions

Happy scripting! ☕
