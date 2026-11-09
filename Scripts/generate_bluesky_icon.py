#!/usr/bin/env python3
"""
Synthesizes the official high-resolution BlueSky Engine icon (1024x1024 and 512x512 PNGs)
and compiles a multi-resolution Apple .icns container.
Features:
- macOS continuous-curvature squircle mask with subpixel anti-aliasing
- Deep cobalt-to-cyan atmospheric sky with cosmic upper haze
- Golden-hour radiant sunburst and bloom over the central alpine saddle
- Faceted twin alpine peaks with sunlit crystalline snow and slate-blue shadow
- Dynamic supersonic aerodynamic engine contrail arc across the upper atmosphere
- Frosted glass rim bevel and subtle specular top highlight
"""
import math
import struct
import zlib
import os

def create_bluesky_icon_png(width=1024, height=1024):
    inv_w = 1.0 / (width - 1)
    inv_h = 1.0 / (height - 1)
    
    raw = bytearray(height * (1 + width * 4))
    
    sun_x, sun_y = 0.50, 0.32
    peak1_x, peak1_y = 0.43, 0.23
    peak2_x, peak2_y = 0.67, 0.33
    
    for y in range(height):
        row_offset = y * (1 + width * 4)
        raw[row_offset] = 0  # PNG filter: None
        v = y * inv_h
        
        for x in range(width):
            u = x * inv_w
            
            # 1. Apple-style 5th-power squircle mask
            nx = (u - 0.5) / 0.435
            ny = (v - 0.5) / 0.435
            dist5 = (abs(nx) ** 5.0 + abs(ny) ** 5.0) ** 0.2
            edge_delta = (1.0 - dist5) * (width * 0.22)
            alpha = max(0.0, min(1.0, edge_delta))
            
            if alpha <= 0.0001:
                continue
                
            # 2. Sky Atmosphere Gradient & Solar Radiance
            sky_factor = v ** 0.85
            sr = 12.0 * (1.0 - sky_factor) + 52.0 * sky_factor
            sg = 22.0 * (1.0 - sky_factor) + 155.0 * sky_factor
            sb = 58.0 * (1.0 - sky_factor) + 232.0 * sky_factor
            
            # Sun radiance / glow behind mountain saddle
            s_dx = (u - sun_x) * 1.3
            s_dy = (v - sun_y) * 1.0
            s_dist = math.sqrt(s_dx * s_dx + s_dy * s_dy)
            sun_glow = math.exp(-s_dist * 4.2) * 0.75
            
            sr += sun_glow * 210.0
            sg += sun_glow * 195.0
            sb += sun_glow * 135.0
            
            # Radial celestial bloom in upper atmosphere
            c_dx = (u - 0.5) * 1.0
            c_dy = (v - 0.18) * 1.0
            c_dist = math.sqrt(c_dx * c_dx + c_dy * c_dy)
            c_glow = math.exp(-c_dist * 2.8) * 0.28
            sr += c_glow * 70.0
            sg += c_glow * 120.0
            sb += c_glow * 190.0
            
            r, g, b = sr, sg, sb
            
            # 3. Aerodynamic Supersonic Contrail / Engine Arc
            arc_t = (u - 0.16) / (0.84 - 0.16)
            if 0.0 <= arc_t <= 1.0:
                arc_y = 0.38 - 0.34 * arc_t + 0.12 * arc_t * arc_t
                arc_dist = abs(v - arc_y)
                if arc_dist < 0.045:
                    arc_intensity = math.exp(-arc_dist * 75.0) * (0.85 - 0.55 * abs(arc_t - 0.5))
                    r = r * (1.0 - arc_intensity * 0.6) + 245.0 * (arc_intensity * 0.6)
                    g = g * (1.0 - arc_intensity * 0.8) + 252.0 * (arc_intensity * 0.8)
                    b = b * (1.0 - arc_intensity * 0.95) + 255.0 * (arc_intensity * 0.95)
            
            # 4. Faceted Twin Mountain Peaks
            p1_left_slope = 1.32
            p1_right_slope = 1.18
            if u < peak1_x:
                m1_y = peak1_y + (peak1_x - u) * p1_left_slope
                m1_side = -1  # Sunlit face
            else:
                m1_y = peak1_y + (u - peak1_x) * p1_right_slope
                m1_side = 1   # Shadowed face
                
            p2_left_slope = 1.25
            p2_right_slope = 1.22
            if u < peak2_x:
                m2_y = peak2_y + (peak2_x - u) * p2_left_slope
                m2_side = -1
            else:
                m2_y = peak2_y + (u - peak2_x) * p2_right_slope
                m2_side = 1
                
            is_m1 = (m1_y <= m2_y)
            mountain_y = min(m1_y, m2_y)
            active_side = m1_side if is_m1 else m2_side
            
            if v >= mountain_y and v <= 0.86:
                m_edge_aa = min(1.0, (v - mountain_y) * (height * 0.45))
                depth_factor = (v - mountain_y) / (0.86 - mountain_y + 1e-5)
                snow_factor = max(0.0, min(1.0, 1.0 - depth_factor * 2.8))
                facet_noise = math.sin(u * 55.0 + v * 35.0) * 0.04 + math.cos(u * 110.0 - v * 80.0) * 0.02
                
                if active_side < 0:
                    # Sunlit snow face (Left): Crystalline alpine snow with warm sun glint
                    base_mr = 230.0 + snow_factor * 25.0 + facet_noise * 30.0
                    base_mg = 240.0 + snow_factor * 15.0 + facet_noise * 25.0
                    base_mb = 252.0 + snow_factor * 3.0 + facet_noise * 20.0
                    
                    rock_r, rock_g, rock_b = 95.0, 135.0, 185.0
                    mr = base_mr * (1.0 - depth_factor * 0.55) + rock_r * (depth_factor * 0.55)
                    mg = base_mg * (1.0 - depth_factor * 0.55) + rock_g * (depth_factor * 0.55)
                    mb = base_mb * (1.0 - depth_factor * 0.50) + rock_b * (depth_factor * 0.50)
                else:
                    # Shadowed slate face (Right): Deep indigo/cobalt slate with cool sky bounce
                    base_mr = 45.0 + snow_factor * 45.0 + facet_noise * 15.0
                    base_mg = 75.0 + snow_factor * 65.0 + facet_noise * 20.0
                    base_mb = 125.0 + snow_factor * 75.0 + facet_noise * 25.0
                    
                    mr = base_mr + 15.0 * (1.0 - depth_factor)
                    mg = base_mg + 25.0 * (1.0 - depth_factor)
                    mb = base_mb + 45.0 * (1.0 - depth_factor)
                
                r = r * (1.0 - m_edge_aa) + mr * m_edge_aa
                g = g * (1.0 - m_edge_aa) + mg * m_edge_aa
                b = b * (1.0 - m_edge_aa) + mb * m_edge_aa
            
            # 5. Valley Morning Mist / Atmospheric Base Layer
            if v > 0.68:
                mist_factor = max(0.0, min(1.0, (v - 0.68) / 0.18))
                mist_r, mist_g, mist_b = 65.0, 140.0, 205.0
                mist_density = mist_factor * 0.42
                r = r * (1.0 - mist_density) + mist_r * mist_density
                g = g * (1.0 - mist_density) + mist_g * mist_density
                b = b * (1.0 - mist_density) + mist_b * mist_density
                
            # 6. Apple-style Frosted Bevel & Highlight Stroke
            if dist5 > 0.88 and dist5 <= 0.995:
                bevel_pos = (dist5 - 0.88) / (0.995 - 0.88)
                bevel_strength = math.sin(bevel_pos * math.pi)
                top_light = max(-0.4, min(1.0, (0.5 - ny) * 1.2))
                if top_light > 0.0:
                    highlight = bevel_strength * top_light * 0.32
                    r = min(255.0, r + 255.0 * highlight)
                    g = min(255.0, g + 255.0 * highlight)
                    b = min(255.0, b + 255.0 * highlight)
                else:
                    rim_shadow = bevel_strength * (-top_light) * 0.25
                    r = max(0.0, r * (1.0 - rim_shadow))
                    g = max(0.0, g * (1.0 - rim_shadow))
                    b = max(0.0, b * (1.0 - rim_shadow))
            
            if dist5 >= 0.97 and dist5 <= 1.0:
                stroke_t = (dist5 - 0.97) / 0.03
                stroke_alpha = math.sin(stroke_t * math.pi) * 0.28
                r = r * (1.0 - stroke_alpha) + 255.0 * stroke_alpha
                g = g * (1.0 - stroke_alpha) + 255.0 * stroke_alpha
                b = b * (1.0 - stroke_alpha) + 255.0 * stroke_alpha
                
            px = row_offset + 1 + x * 4
            raw[px] = int(max(0.0, min(255.0, r)))
            raw[px + 1] = int(max(0.0, min(255.0, g)))
            raw[px + 2] = int(max(0.0, min(255.0, b)))
            raw[px + 3] = int(max(0.0, min(255.0, alpha * 255.0)))
            
    # Assemble standard PNG
    out = bytearray(b'\x89PNG\r\n\x1a\n')
    ihdr = struct.pack('>IIBBBBB', width, height, 8, 6, 0, 0, 0)
    out.extend(struct.pack('>I', len(ihdr)))
    out.extend(b'IHDR')
    out.extend(ihdr)
    out.extend(struct.pack('>I', zlib.crc32(b'IHDR' + ihdr)))
    
    compressed = zlib.compress(bytes(raw), level=9)
    out.extend(struct.pack('>I', len(compressed)))
    out.extend(b'IDAT')
    out.extend(compressed)
    out.extend(struct.pack('>I', zlib.crc32(b'IDAT' + compressed)))
    
    out.extend(struct.pack('>I', 0))
    out.extend(b'IEND')
    out.extend(struct.pack('>I', zlib.crc32(b'IEND')))
    
    return bytes(out)

def build_apple_icns(png_dict):
    """
    Builds a native macOS .icns container file holding PNG image data chunks.
    Tags:
      'ic07': 128x128
      'ic08': 256x256
      'ic09': 512x512
      'ic10': 1024x1024 (512x512@2x)
      'ic11': 16x16@2x (32x32)
      'ic12': 32x32@2x (64x64)
      'ic13': 128x128@2x (256x256)
      'ic14': 256x256@2x (512x512)
    """
    chunks = []
    for tag, png_bytes in png_dict.items():
        chunk_len = len(png_bytes) + 8
        chunks.append(tag.encode('ascii') + struct.pack('>I', chunk_len) + png_bytes)
    body = b''.join(chunks)
    header = b'icns' + struct.pack('>I', len(body) + 8)
    return header + body

if __name__ == '__main__':
    root_dir = "/Users/sohamanand/BlueSky Engine"
    assets_dir = os.path.join(root_dir, "Assets")
    engine_dir = os.path.join(root_dir, "BlueSkyEngine")
    builds_dir = os.path.join(root_dir, "Builds", "MacOS", "BlueSkyGame.app", "Contents", "Resources")
    
    os.makedirs(assets_dir, exist_ok=True)
    os.makedirs(builds_dir, exist_ok=True)
    
    print("==> 1. Generating 1024x1024 Master Icon (BlueSkyEngineIcon.png)...")
    icon1024 = create_bluesky_icon_png(1024, 1024)
    with open(os.path.join(assets_dir, "BlueSkyEngineIcon.png"), "wb") as f:
        f.write(icon1024)
    with open(os.path.join(root_dir, "BlueSkyEngineIcon.png"), "wb") as f:
        f.write(icon1024)
    print("  ✓ Assets/BlueSkyEngineIcon.png (1024x1024)")
        
    print("==> 2. Generating 512x512 App Icon (AppIcon.png)...")
    icon512 = create_bluesky_icon_png(512, 512)
    with open(os.path.join(assets_dir, "AppIcon.png"), "wb") as f:
        f.write(icon512)
    with open(os.path.join(engine_dir, "AppIcon.png"), "wb") as f:
        f.write(icon512)
    with open(os.path.join(builds_dir, "AppIcon.png"), "wb") as f:
        f.write(icon512)
    print("  ✓ Assets/AppIcon.png (512x512)")
        
    print("==> 3. Generating multi-resolution PNGs and assembling native macOS AppIcon.icns...")
    icon256 = create_bluesky_icon_png(256, 256)
    icon128 = create_bluesky_icon_png(128, 128)
    icon64 = create_bluesky_icon_png(64, 64)
    icon32 = create_bluesky_icon_png(32, 32)
    
    icns_dict = {
        'ic10': icon1024,
        'ic09': icon512,
        'ic14': icon512,
        'ic08': icon256,
        'ic13': icon256,
        'ic07': icon128,
        'ic12': icon64,
        'ic11': icon32
    }
    
    icns_data = build_apple_icns(icns_dict)
    
    icns_path = os.path.join(assets_dir, "AppIcon.icns")
    res_icns = os.path.join(builds_dir, "AppIcon.icns")
    
    with open(icns_path, "wb") as f:
        f.write(icns_data)
    with open(res_icns, "wb") as f:
        f.write(icns_data)
    with open(os.path.join(engine_dir, "AppIcon.icns"), "wb") as f:
        f.write(icns_data)
        
    print(f"  ✓ Compiled native macOS AppIcon.icns ({len(icns_data) / 1024:.1f} KB)")
    print("✓ Successfully generated all high-resolution BlueSky Engine icons!")
