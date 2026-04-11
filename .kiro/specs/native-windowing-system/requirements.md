# Requirements Document: Native Windowing System

## Introduction

The BlueSky Engine currently uses Silk.NET for cross-platform windowing, but this has proven incompatible with the bgfx rendering library on macOS. Specifically, bgfx cannot initialize its Metal backend with Silk.NET-created windows, resulting in "invalid pixel format 0" crashes during CAMetalLayer initialization. This document specifies requirements for replacing Silk.NET with native windowing APIs that provide proper integration with bgfx across all target platforms (macOS, Windows, Linux).

## Glossary

- **Native_Window_System**: The platform-specific windowing implementation (Cocoa on macOS, Win32 on Windows, X11/Wayland on Linux)
- **Window_Manager**: The cross-platform abstraction layer that provides a unified interface to native windowing systems
- **Input_System**: The platform-specific input handling implementation for keyboard, mouse, and touch events
- **Renderer_Backend**: The graphics API used by bgfx (Metal, DirectX 11, Vulkan, or OpenGL)
- **Window_Handle**: A platform-specific native window identifier (NSWindow on macOS, HWND on Windows, Window on X11)
- **Event_Loop**: The platform-specific message pump that processes window and input events
- **bgfx**: The cross-platform rendering library that requires native window handles for initialization
- **Silk.NET**: The current windowing library being replaced due to bgfx incompatibility

## Requirements

### Requirement 1: Remove Silk.NET Dependencies

**User Story:** As a developer, I want all Silk.NET dependencies removed from the project, so that the engine no longer relies on incompatible windowing libraries.

#### Acceptance Criteria

1. THE Window_Manager SHALL NOT reference any Silk.NET packages
2. THE Window_Manager SHALL NOT use any Silk.NET types or APIs
3. WHEN the project is built, THE build system SHALL NOT include Silk.NET assemblies
4. THE BlueSky.Rendering project SHALL remove Silk.NET.Windowing package references
5. THE NotBSUI project SHALL remove Silk.NET.Windowing package references
6. THE BlueSky.Editor project SHALL remove Silk.NET.Windowing package references

### Requirement 2: macOS Native Window Creation

**User Story:** As a macOS user, I want windows created using native Cocoa APIs, so that bgfx can properly initialize the Metal rendering backend.

#### Acceptance Criteria

1. WHEN running on macOS, THE Native_Window_System SHALL create windows using NSWindow
2. WHEN running on macOS, THE Native_Window_System SHALL create an NSView for rendering
3. WHEN running on macOS, THE Native_Window_System SHALL create a CAMetalLayer with pixel format BGRA8Unorm (80)
4. WHEN bgfx requests a window handle on macOS, THE Native_Window_System SHALL provide the NSView pointer
5. WHEN a CAMetalLayer is created, THE Native_Window_System SHALL set the pixel format before passing to bgfx
6. THE Native_Window_System SHALL use Objective-C runtime interop for Cocoa API calls
7. WHEN the window is resized on macOS, THE Native_Window_System SHALL update the CAMetalLayer bounds

### Requirement 3: Windows Native Window Creation

**User Story:** As a Windows user, I want windows created using native Win32 APIs, so that bgfx can properly initialize the DirectX rendering backend.

#### Acceptance Criteria

1. WHEN running on Windows, THE Native_Window_System SHALL create windows using CreateWindowEx
2. WHEN running on Windows, THE Native_Window_System SHALL register a window class with appropriate styles
3. WHEN bgfx requests a window handle on Windows, THE Native_Window_System SHALL provide the HWND
4. THE Native_Window_System SHALL implement a window procedure to handle Win32 messages
5. WHEN the window is resized on Windows, THE Native_Window_System SHALL handle WM_SIZE messages
6. THE Native_Window_System SHALL use P/Invoke for Win32 API calls

### Requirement 4: Linux Native Window Creation

**User Story:** As a Linux user, I want windows created using X11 or Wayland APIs, so that bgfx can properly initialize the Vulkan or OpenGL rendering backend.

#### Acceptance Criteria

1. WHEN running on Linux, THE Native_Window_System SHALL create windows using X11 or Wayland APIs
2. WHEN bgfx requests a window handle on Linux, THE Native_Window_System SHALL provide the X11 Window handle or Wayland surface
3. THE Native_Window_System SHALL detect whether X11 or Wayland is available
4. WHEN the window is resized on Linux, THE Native_Window_System SHALL handle ConfigureNotify events (X11) or surface configure events (Wayland)
5. THE Native_Window_System SHALL use P/Invoke for X11/Wayland API calls

### Requirement 5: Cross-Platform Window Abstraction

**User Story:** As a developer, I want a unified window interface across all platforms, so that engine code does not need platform-specific conditionals.

#### Acceptance Criteria

1. THE Window_Manager SHALL provide a platform-agnostic IWindow interface
2. THE IWindow interface SHALL expose window size, position, and title properties
3. THE IWindow interface SHALL provide methods for showing, hiding, and closing windows
4. THE IWindow interface SHALL provide a method to retrieve the native window handle
5. WHEN engine code requests window properties, THE Window_Manager SHALL return values without exposing platform-specific types
6. THE Window_Manager SHALL implement platform-specific window classes that conform to the IWindow interface

### Requirement 6: Native Input Handling - Keyboard

**User Story:** As a user, I want keyboard input handled natively per platform, so that all keyboard events are captured correctly.

#### Acceptance Criteria

1. WHEN a key is pressed, THE Input_System SHALL generate a key down event
2. WHEN a key is released, THE Input_System SHALL generate a key up event
3. THE Input_System SHALL map platform-specific key codes to a unified KeyCode enumeration
4. THE Input_System SHALL track modifier key states (Shift, Control, Alt, Command)
5. WHEN text input occurs, THE Input_System SHALL generate character input events with Unicode support
6. THE Input_System SHALL handle key repeat events according to platform conventions

### Requirement 7: Native Input Handling - Mouse

**User Story:** As a user, I want mouse input handled natively per platform, so that all mouse events are captured correctly.

#### Acceptance Criteria

1. WHEN the mouse moves, THE Input_System SHALL generate mouse move events with window-relative coordinates
2. WHEN a mouse button is pressed, THE Input_System SHALL generate mouse button down events
3. WHEN a mouse button is released, THE Input_System SHALL generate mouse button up events
4. WHEN the mouse wheel scrolls, THE Input_System SHALL generate scroll events with delta values
5. THE Input_System SHALL support at least left, right, and middle mouse buttons
6. THE Input_System SHALL track mouse position relative to the window client area

### Requirement 8: Native Input Handling - Touch (Optional)

**User Story:** As a user on a touch-enabled device, I want touch input handled natively, so that touch gestures work correctly.

#### Acceptance Criteria

1. WHERE touch input is available, WHEN a touch begins, THE Input_System SHALL generate touch down events
2. WHERE touch input is available, WHEN a touch moves, THE Input_System SHALL generate touch move events
3. WHERE touch input is available, WHEN a touch ends, THE Input_System SHALL generate touch up events
4. WHERE touch input is available, THE Input_System SHALL support multi-touch with unique touch identifiers
5. WHERE touch input is available, THE Input_System SHALL provide touch coordinates relative to the window

### Requirement 9: Platform-Specific Event Loops

**User Story:** As a developer, I want native event loops per platform, so that window and input events are processed efficiently.

#### Acceptance Criteria

1. WHEN running on macOS, THE Event_Loop SHALL use NSApplication run loop or manual event polling
2. WHEN running on Windows, THE Event_Loop SHALL use GetMessage/DispatchMessage or PeekMessage
3. WHEN running on Linux, THE Event_Loop SHALL use XPending/XNextEvent (X11) or wl_display_dispatch (Wayland)
4. THE Event_Loop SHALL process events without blocking the rendering thread
5. WHEN the application requests shutdown, THE Event_Loop SHALL terminate gracefully
6. THE Event_Loop SHALL dispatch events to registered event handlers

### Requirement 10: bgfx Integration

**User Story:** As a developer, I want native windows to work seamlessly with bgfx, so that rendering initialization succeeds on all platforms.

#### Acceptance Criteria

1. WHEN bgfx initializes on macOS, THE Native_Window_System SHALL provide a valid NSView with CAMetalLayer
2. WHEN bgfx initializes on Windows, THE Native_Window_System SHALL provide a valid HWND
3. WHEN bgfx initializes on Linux, THE Native_Window_System SHALL provide a valid X11 Window or Wayland surface
4. THE Native_Window_System SHALL ensure window handles are valid before bgfx initialization
5. WHEN bgfx requests the window size, THE Native_Window_System SHALL provide accurate dimensions
6. IF bgfx initialization fails, THEN THE Native_Window_System SHALL log detailed error information

### Requirement 11: Window Lifecycle Management

**User Story:** As a developer, I want proper window lifecycle management, so that resources are created and destroyed correctly.

#### Acceptance Criteria

1. WHEN a window is created, THE Window_Manager SHALL allocate all required native resources
2. WHEN a window is closed, THE Window_Manager SHALL release all native resources
3. THE Window_Manager SHALL prevent use of destroyed window handles
4. WHEN the application exits, THE Window_Manager SHALL ensure all windows are properly destroyed
5. THE Window_Manager SHALL support creating multiple windows simultaneously
6. WHEN a window is destroyed, THE Window_Manager SHALL notify registered event handlers

### Requirement 12: Window Properties and Configuration

**User Story:** As a developer, I want to configure window properties, so that windows can be customized for different use cases.

#### Acceptance Criteria

1. THE Window_Manager SHALL support setting window title
2. THE Window_Manager SHALL support setting window size (width and height)
3. THE Window_Manager SHALL support setting window position (x and y coordinates)
4. THE Window_Manager SHALL support enabling/disabling window resizing
5. THE Window_Manager SHALL support fullscreen mode
6. THE Window_Manager SHALL support VSync configuration
7. WHEN window properties are changed, THE Window_Manager SHALL apply changes immediately

### Requirement 13: Window Events

**User Story:** As a developer, I want to receive window events, so that the application can respond to window state changes.

#### Acceptance Criteria

1. WHEN a window is resized, THE Window_Manager SHALL generate a resize event with new dimensions
2. WHEN a window gains focus, THE Window_Manager SHALL generate a focus gained event
3. WHEN a window loses focus, THE Window_Manager SHALL generate a focus lost event
4. WHEN a window close is requested, THE Window_Manager SHALL generate a closing event
5. WHEN a window is minimized, THE Window_Manager SHALL generate a minimize event
6. WHEN a window is restored, THE Window_Manager SHALL generate a restore event
7. THE Window_Manager SHALL allow event handlers to be registered and unregistered

### Requirement 14: Error Handling and Diagnostics

**User Story:** As a developer, I want comprehensive error handling, so that windowing issues can be diagnosed and resolved.

#### Acceptance Criteria

1. WHEN a native API call fails, THE Window_Manager SHALL log the error with platform-specific error codes
2. WHEN window creation fails, THE Window_Manager SHALL throw an exception with a descriptive message
3. THE Window_Manager SHALL validate window handles before use
4. WHEN an invalid window handle is used, THE Window_Manager SHALL throw an exception
5. THE Window_Manager SHALL log all window lifecycle events at appropriate log levels
6. WHEN running in debug mode, THE Window_Manager SHALL provide verbose diagnostic output

### Requirement 15: Renderer Backend Detection Integration

**User Story:** As a developer, I want the window system to work with the existing renderer detection, so that the correct graphics API is used per platform.

#### Acceptance Criteria

1. WHEN the Renderer_Backend is Metal, THE Window_Manager SHALL create macOS windows with CAMetalLayer support
2. WHEN the Renderer_Backend is DirectX 11, THE Window_Manager SHALL create Windows windows compatible with DirectX
3. WHEN the Renderer_Backend is Vulkan, THE Window_Manager SHALL create Linux windows compatible with Vulkan
4. WHEN the Renderer_Backend is OpenGL, THE Window_Manager SHALL create windows compatible with OpenGL contexts
5. THE Window_Manager SHALL query the RendererDetection system to determine the appropriate backend
6. THE Window_Manager SHALL configure window properties based on the selected Renderer_Backend

### Requirement 16: NotBSUI Integration

**User Story:** As a developer, I want the NotBSUI system to work with native windowing, so that the UI continues to function after Silk.NET removal.

#### Acceptance Criteria

1. THE Input_System SHALL provide mouse position data compatible with NotBSUI's InputManager
2. THE Input_System SHALL provide mouse button events compatible with NotBSUI's InputManager
3. THE Window_Manager SHALL provide window dimensions for NotBSUI layout calculations
4. WHEN NotBSUI requests input state, THE Input_System SHALL provide current mouse and keyboard state
5. THE Input_System SHALL replace Silk.NET.Input.IInputContext with a native equivalent
6. THE Input_System SHALL maintain the same event-driven input model used by NotBSUI

### Requirement 17: Performance Requirements

**User Story:** As a user, I want efficient window and input handling, so that the application remains responsive.

#### Acceptance Criteria

1. WHEN processing input events, THE Event_Loop SHALL complete processing within 1 millisecond per event
2. WHEN resizing a window, THE Window_Manager SHALL update window state within 16 milliseconds (60 FPS)
3. THE Event_Loop SHALL NOT block the rendering thread
4. THE Input_System SHALL batch input events when multiple events occur in a single frame
5. THE Window_Manager SHALL minimize memory allocations during event processing
6. WHEN polling for events, THE Event_Loop SHALL use efficient platform-specific mechanisms

### Requirement 18: Thread Safety

**User Story:** As a developer, I want thread-safe window operations, so that the window system can be used from multiple threads safely.

#### Acceptance Criteria

1. THE Window_Manager SHALL ensure window creation is thread-safe
2. THE Window_Manager SHALL ensure window destruction is thread-safe
3. WHEN window properties are accessed from multiple threads, THE Window_Manager SHALL prevent race conditions
4. THE Event_Loop SHALL process events on the main thread
5. WHEN rendering occurs on a separate thread, THE Window_Manager SHALL coordinate with the Event_Loop safely
6. THE Window_Manager SHALL document which operations are thread-safe

### Requirement 19: Backward Compatibility Shim (Optional)

**User Story:** As a developer, I want a compatibility layer for existing Silk.NET-dependent code, so that migration can be gradual.

#### Acceptance Criteria

1. WHERE backward compatibility is needed, THE Window_Manager SHALL provide adapter classes mimicking Silk.NET interfaces
2. WHERE backward compatibility is needed, THE Window_Manager SHALL map native window types to Silk.NET-compatible types
3. WHERE backward compatibility is needed, THE Window_Manager SHALL provide deprecation warnings for compatibility APIs
4. WHERE backward compatibility is needed, THE Window_Manager SHALL document migration paths from compatibility APIs to native APIs

### Requirement 20: Testing and Validation

**User Story:** As a developer, I want comprehensive testing of the windowing system, so that platform-specific issues are caught early.

#### Acceptance Criteria

1. THE Window_Manager SHALL include unit tests for window lifecycle operations
2. THE Window_Manager SHALL include integration tests for bgfx initialization on each platform
3. THE Input_System SHALL include tests for keyboard and mouse event handling
4. THE Window_Manager SHALL include tests for window resize and property changes
5. THE Window_Manager SHALL include tests for multi-window scenarios
6. WHEN tests run on each platform, THE test suite SHALL verify platform-specific behavior
7. THE Window_Manager SHALL include manual test procedures for visual validation
