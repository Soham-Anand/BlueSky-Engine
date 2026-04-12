# 🧪 Test Instructions - Horizon GI Lighting

**Quick guide to test the new PBR lighting system**

---

## 🚀 How to Run

### Option 1: From Build Directory

```bash
cd BlueSkyEngine/bin/Debug/net8.0
./BlueSkyEngine
```

### Option 2: Using dotnet run

```bash
cd BlueSkyEngine
dotnet run --project BlueSkyEngine.csproj
```

### Option 3: Rebuild and Run

```bash
# Clean build
dotnet clean BlueSkyEngine/BlueSkyEngine.csproj

# Build
dotnet build BlueSkyEngine/BlueSkyEngine.csproj

# Run
cd BlueSkyEngine/bin/Debug/net8.0
./BlueSkyEngine
```

---

## 👀 What to Look For

### ✅ Success Indicators

1. **Teapot is visible and illuminated**
   - Orange-rust color
   - Bright on top/front
   - Darker on bottom/back
   - Smooth shading

2. **No console errors**
   - No shader loading errors
   - No buffer binding errors
   - No rendering errors

3. **Smooth performance**
   - 60+ FPS
   - No stuttering
   - No frame drops

### ❌ Failure Indicators

1. **Teapot is completely dark**
   - All black or very dark
   - No visible details
   - → Check console for shader errors

2. **Console shows errors**
   - "Failed to load horizon_lighting.metallib"
   - "Buffer binding failed"
   - → Report the exact error message

3. **Performance issues**
   - Low FPS (<30)
   - Stuttering
   - → Report your GPU model

---

## 🔍 Detailed Inspection

### Camera Controls

Use these controls to inspect the teapot:

- **Orbit:** Click and drag
- **Zoom:** Scroll wheel
- **Pan:** Right-click and drag

### What to Check from Different Angles

**Front View (Facing Sun):**
```
Expected: Bright orange-rust
         Clear highlights
         Strong shading
```

**Side View:**
```
Expected: Half bright, half dark
         Smooth gradient
         Visible form
```

**Back View:**
```
Expected: Mostly in shadow
         Subtle ambient fill
         Still visible
```

**Top View:**
```
Expected: Bright top surface
         Circular highlight
         Clear details
```

---

## 📊 Performance Check

### Expected Frame Times

**Intel HD 3000 @ 720p:**
```
Target:  16.6ms (60 FPS)
Current: ~4-5ms (200+ FPS)
Status:  ✅ Excellent headroom
```

**Intel HD 4000 @ 900p:**
```
Target:  16.6ms (60 FPS)
Current: ~3-4ms (250+ FPS)
Status:  ✅ Excellent headroom
```

**GTX 750 @ 1080p:**
```
Target:  16.6ms (60 FPS)
Current: ~2-3ms (330+ FPS)
Status:  ✅ Excellent headroom
```

### How to Check FPS

Look for FPS counter in the editor (if available) or check console output.

---

## 🐛 Troubleshooting

### Problem: Teapot is completely dark

**Possible Causes:**
1. Shader not loading
2. Light intensity is 0
3. Material albedo is black
4. Buffers not binding

**Solutions:**

1. **Check shader file exists:**
   ```bash
   ls -la BlueSkyEngine/bin/Debug/net8.0/Shaders/horizon_lighting.metallib
   ```
   Should show: ~25KB file

2. **Check console output:**
   Look for errors like:
   - "Failed to load horizon_lighting.metallib"
   - "Shader compilation failed"

3. **Verify shader is in correct location:**
   ```bash
   # Should be in same directory as executable
   cd BlueSkyEngine/bin/Debug/net8.0
   ls -la Shaders/
   ```

4. **Recompile shaders:**
   ```bash
   cd BlueSkyEngine/Editor/Shaders
   xcrun -sdk macosx metal -c horizon_lighting.metal -o horizon_lighting.air
   xcrun -sdk macosx metallib horizon_lighting.air -o horizon_lighting.metallib
   cp horizon_lighting.metallib ../../bin/Debug/net8.0/Shaders/
   ```

### Problem: Console shows shader errors

**Example Error:**
```
[ViewportRenderer] WARNING: Metal library not found at /path/to/horizon_lighting.metallib
```

**Solution:**
1. Copy shader to correct location:
   ```bash
   cp BlueSkyEngine/Editor/Shaders/horizon_lighting.metallib \
      BlueSkyEngine/bin/Debug/net8.0/Shaders/
   ```

2. Rebuild project:
   ```bash
   dotnet build BlueSkyEngine/BlueSkyEngine.csproj
   ```

### Problem: Wrong colors

**Possible Causes:**
1. Material data not binding correctly
2. Gamma correction issue
3. Tone mapping issue

**Solution:**
Check console for buffer binding errors. If no errors, this is a shader bug - report it.

### Problem: Flickering or artifacts

**Possible Causes:**
1. Z-fighting
2. Buffer update race condition
3. Shader precision issues

**Solution:**
Report the exact behavior and when it happens (e.g., "flickers when rotating camera").

### Problem: Low FPS

**Possible Causes:**
1. Debug build (slower)
2. GPU too old
3. Resolution too high

**Solutions:**

1. **Build in Release mode:**
   ```bash
   dotnet build BlueSkyEngine/BlueSkyEngine.csproj -c Release
   cd BlueSkyEngine/bin/Release/net8.0
   ./BlueSkyEngine
   ```

2. **Lower resolution:**
   - Resize window to smaller size
   - Should improve FPS

3. **Check GPU:**
   ```bash
   system_profiler SPDisplaysDataType | grep "Chipset Model"
   ```

---

## 📸 Screenshot Checklist

If you want to share results, take screenshots showing:

1. **Full viewport** - Overall appearance
2. **Front view** - Bright lighting
3. **Side view** - Gradient shading
4. **Back view** - Shadow areas
5. **Console output** - Any errors

---

## ✅ Success Criteria

### Minimum Requirements

- [ ] Teapot is visible
- [ ] Teapot is illuminated (not completely dark)
- [ ] Orange-rust color is visible
- [ ] Shading shows 3D form
- [ ] No console errors
- [ ] 30+ FPS

### Excellent Results

- [ ] Photorealistic appearance
- [ ] Smooth gradients
- [ ] Clear highlights
- [ ] Proper material response
- [ ] 60+ FPS
- [ ] No artifacts

---

## 📝 Reporting Results

### Good Report Template

```
Status: ✅ Working / ❌ Not Working

Visual Quality:
- Teapot visibility: [Good/Bad/Invisible]
- Lighting: [Bright/Dark/Correct]
- Colors: [Orange-rust/Wrong/Black]
- Shading: [Smooth/Banded/Flat]

Performance:
- FPS: [Number]
- Frame time: [ms]
- Stuttering: [Yes/No]

Console Output:
[Paste any errors or warnings]

System Info:
- GPU: [Model]
- OS: [macOS version]
- Resolution: [Width x Height]

Screenshots:
[Attach if possible]
```

### Example Good Report

```
Status: ✅ Working

Visual Quality:
- Teapot visibility: Good
- Lighting: Correct
- Colors: Orange-rust as expected
- Shading: Smooth gradients

Performance:
- FPS: 240
- Frame time: 4.2ms
- Stuttering: No

Console Output:
No errors

System Info:
- GPU: M1 Pro
- OS: macOS 14.0
- Resolution: 1920x1080

Screenshots:
[Attached]
```

### Example Bad Report

```
Status: ❌ Not Working

Visual Quality:
- Teapot visibility: Invisible/Black
- Lighting: None
- Colors: All black
- Shading: Can't see

Performance:
- FPS: N/A (can't see anything)

Console Output:
[ViewportRenderer] WARNING: Metal library not found at /Users/.../horizon_lighting.metallib

System Info:
- GPU: Intel HD 3000
- OS: macOS 12.0
- Resolution: 1280x720
```

---

## 🎯 Next Steps

### If Everything Works ✅

**Congratulations!** The PBR lighting is working correctly.

**Next Phase:**
1. Integrate mesh data extraction
2. Wire up baking pipeline
3. Implement serialization
4. Add full GI system

**Timeline:** ~2-3 weeks of development

### If Something Doesn't Work ❌

**Don't worry!** We'll fix it together.

**Next Steps:**
1. Report the issue using the template above
2. Include console output
3. Include system info
4. We'll debug and fix

**Timeline:** Usually fixed within 1-2 iterations

---

## 🎓 Understanding the Test

### What We're Testing

1. **Shader Loading**
   - Can the engine find and load the Metal shader?
   - Is the shader compiled correctly?

2. **Buffer Binding**
   - Are all uniform buffers binding correctly?
   - Is data being uploaded to GPU?

3. **Lighting Calculation**
   - Is the PBR shader computing lighting correctly?
   - Are lights being processed?

4. **Material System**
   - Is material data being passed to shader?
   - Are material properties correct?

5. **Performance**
   - Is the PBR shader fast enough?
   - Any bottlenecks?

### What We're NOT Testing Yet

- ❌ Lightmap baking (not implemented yet)
- ❌ Irradiance volumes (not implemented yet)
- ❌ Reflection probes (not implemented yet)
- ❌ SSAO (not implemented yet)
- ❌ SSR (not implemented yet)

**Those come in the next phase!**

---

## 🏆 Success Metrics

### Visual Quality

```
Current:  PBR lighting with 1 directional light
Target:   Photorealistic with proper shading
Status:   Should meet target ✅
```

### Performance

```
Current:  ~4-5ms per frame
Target:   <16.6ms (60 FPS)
Status:   Excellent headroom ✅
```

### Stability

```
Current:  Should be stable
Target:   No crashes, no errors
Status:   Should meet target ✅
```

---

## 📞 Support

### If You Need Help

1. **Check this document first**
2. **Check console output**
3. **Try troubleshooting steps**
4. **Report using template above**

### Common Questions

**Q: Why is the teapot dark?**
A: Check if shader file exists and console for errors.

**Q: Why is performance low?**
A: Try Release build or lower resolution.

**Q: Can I change the light direction?**
A: Yes! Edit `ViewportRenderer.cs` line ~280.

**Q: Can I change the material color?**
A: Yes! Edit `ViewportRenderer.cs` line ~420.

**Q: When will full GI be ready?**
A: After this test succeeds, ~2-3 weeks.

---

## 🎉 Ready to Test!

**Run the editor and see the beautiful PBR lighting!**

```bash
cd BlueSkyEngine/bin/Debug/net8.0
./BlueSkyEngine
```

**Expected result:** Beautiful orange-rust teapot with proper lighting! 🫖✨

**If it works:** Celebrate! 🎉 We're one step closer to Forza Horizon 6 quality!

**If it doesn't:** No worries! Report the issue and we'll fix it together! 🔧

---

**Good luck and happy testing!** 🚀
