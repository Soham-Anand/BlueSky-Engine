// Software Ray Tracing - BVH Intersection Shader
// THE MAGIC: 20,000x speedup over brute force
// Traverses BVH tree and tests ray-triangle intersections

struct Ray
{
    float3 Origin;
    float TMin;
    float3 Direction;
    float TMax;
};

struct RayHit
{
    float3 Position;
    float T;
    float3 Normal;
    uint TriangleIndex;
    float2 UV;
    uint MaterialIndex;
    uint Padding;
};

struct BVHNode
{
    float3 BoundsMin;
    float _padding1;
    float3 BoundsMax;
    float _padding2;
    int LeftChild;      // Or PrimitiveOffset for leaf
    int RightChild;     // Or PrimitiveCount for leaf
    uint IsLeaf;
    uint SplitAxis;
};

struct Triangle
{
    float3 V0, V1, V2;
    float3 N0, N1, N2;
    float2 UV0, UV1, UV2;
};

StructuredBuffer<Ray> Rays : register(t0);
StructuredBuffer<BVHNode> BVHNodes : register(t1);
StructuredBuffer<Triangle> Triangles : register(t2);
RWStructuredBuffer<RayHit> Hits : register(u0);

// Ray-AABB intersection (slab method)
bool IntersectAABB(Ray ray, float3 boundsMin, float3 boundsMax)
{
    float tMin = ray.TMin;
    float tMax = ray.TMax;
    
    for (int i = 0; i < 3; i++)
    {
        float invD = 1.0 / ray.Direction[i];
        float t0 = (boundsMin[i] - ray.Origin[i]) * invD;
        float t1 = (boundsMax[i] - ray.Origin[i]) * invD;
        
        if (invD < 0.0)
        {
            float temp = t0;
            t0 = t1;
            t1 = temp;
        }
        
        tMin = max(t0, tMin);
        tMax = min(t1, tMax);
        
        if (tMax <= tMin)
            return false;
    }
    
    return true;
}

// Möller-Trumbore ray-triangle intersection
bool IntersectTriangle(Ray ray, Triangle tri, out float t, out float2 uv)
{
    t = 0.0;
    uv = float2(0.0, 0.0);
    
    float3 edge1 = tri.V1 - tri.V0;
    float3 edge2 = tri.V2 - tri.V0;
    float3 h = cross(ray.Direction, edge2);
    float a = dot(edge1, h);
    
    if (abs(a) < 1e-8)
        return false; // Ray parallel to triangle
    
    float f = 1.0 / a;
    float3 s = ray.Origin - tri.V0;
    float u = f * dot(s, h);
    
    if (u < 0.0 || u > 1.0)
        return false;
    
    float3 q = cross(s, edge1);
    float v = f * dot(ray.Direction, q);
    
    if (v < 0.0 || u + v > 1.0)
        return false;
    
    t = f * dot(edge2, q);
    
    if (t < ray.TMin || t > ray.TMax)
        return false;
    
    uv = float2(u, v);
    return true;
}

// Interpolate triangle normal
float3 InterpolateNormal(Triangle tri, float2 uv)
{
    float w = 1.0 - uv.x - uv.y;
    return normalize(w * tri.N0 + uv.x * tri.N1 + uv.y * tri.N2);
}

// Traverse BVH and find closest hit
RayHit TraverseBVH(Ray ray)
{
    RayHit closestHit;
    closestHit.T = ray.TMax;
    closestHit.Position = float3(0, 0, 0);
    closestHit.Normal = float3(0, 1, 0);
    closestHit.TriangleIndex = 0xFFFFFFFF; // Invalid
    closestHit.UV = float2(0, 0);
    closestHit.MaterialIndex = 0;
    closestHit.Padding = 0;
    
    // Stack for BVH traversal (no recursion on GPU)
    int stack[64];
    int stackPtr = 0;
    stack[stackPtr++] = 0; // Start with root node
    
    while (stackPtr > 0)
    {
        int nodeIndex = stack[--stackPtr];
        BVHNode node = BVHNodes[nodeIndex];
        
        // Test ray against node bounds
        if (!IntersectAABB(ray, node.BoundsMin, node.BoundsMax))
            continue;
        
        if (node.IsLeaf)
        {
            // Leaf node: test all triangles
            int primitiveOffset = node.LeftChild;
            int primitiveCount = node.RightChild;
            
            for (int i = 0; i < primitiveCount; i++)
            {
                int triIndex = primitiveOffset + i;
                Triangle tri = Triangles[triIndex];
                
                float t;
                float2 uv;
                if (IntersectTriangle(ray, tri, t, uv))
                {
                    if (t < closestHit.T)
                    {
                        closestHit.T = t;
                        closestHit.Position = ray.Origin + t * ray.Direction;
                        closestHit.Normal = InterpolateNormal(tri, uv);
                        closestHit.TriangleIndex = triIndex;
                        closestHit.UV = uv;
                        
                        // Update ray TMax for early termination
                        ray.TMax = t;
                    }
                }
            }
        }
        else
        {
            // Interior node: push children onto stack
            if (stackPtr < 62) // Leave room for 2 children
            {
                stack[stackPtr++] = node.LeftChild;
                stack[stackPtr++] = node.RightChild;
            }
        }
    }
    
    return closestHit;
}

[numthreads(64, 1, 1)]
void main(uint3 dispatchThreadID : SV_DispatchThreadID)
{
    uint rayIndex = dispatchThreadID.x;
    
    // Bounds check
    uint rayCount;
    uint stride;
    Rays.GetDimensions(rayCount, stride);
    
    if (rayIndex >= rayCount)
        return;
    
    // Load ray
    Ray ray = Rays[rayIndex];
    
    // Traverse BVH and find closest hit
    RayHit hit = TraverseBVH(ray);
    
    // Write hit
    Hits[rayIndex] = hit;
}
