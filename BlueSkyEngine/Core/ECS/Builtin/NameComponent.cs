using BlueSky.Core.Math;

namespace BlueSky.Core.ECS.Builtin
{
    /// <summary>
    /// Name component for entity identification in editor and debugging.
    /// </summary>
    public unsafe struct NameComponent
    {
        private fixed char _name[64];
        
        public string Name
        {
            get
            {
                fixed (char* ptr = _name)
                {
                    return new string(ptr);
                }
            }
        }
        
        public void SetName(string name)
        {
            name ??= string.Empty;
            int length = System.Math.Min(63, name.Length);
            fixed (char* ptr = _name)
            {
                for (int i = 0; i < length; i++)
                {
                    ptr[i] = name[i];
                }
                ptr[length] = '\0';
            }
        }
        
        public NameComponent(string name) 
        { 
            _name[0] = '\0'; // Initialize first char
            SetName(name);
        }
        
        public static implicit operator NameComponent(string name) => new(name);
        public static implicit operator string(NameComponent name) => name.Name;
    }

    /// <summary>
    /// Camera component for viewport rendering.
    /// </summary>
    public struct CameraComponent
    {
        public float FieldOfView;
        public float NearPlane;
        public float FarPlane;
        public float AspectRatio;
        public bool IsOrthographic;
        public float OrthographicSize;
        
        public CameraComponent(float fov = 60f, float near = 0.1f, float far = 1000f)
        {
            FieldOfView = fov;
            NearPlane = near;
            FarPlane = far;
            AspectRatio = 16f / 9f;
            IsOrthographic = false;
            OrthographicSize = 5f;
        }

        public Matrix4x4 GetProjectionMatrix()
        {
            if (IsOrthographic)
            {
                float left = -OrthographicSize * AspectRatio;
                float right = OrthographicSize * AspectRatio;
                float bottom = -OrthographicSize;
                float top = OrthographicSize;
                
                return new Matrix4x4(
                    2f / (right - left), 0, 0, 0,
                    0, 2f / (top - bottom), 0, 0,
                    0, 0, -2f / (FarPlane - NearPlane), 0,
                    -(right + left) / (right - left), -(top + bottom) / (top - bottom), 
                    -(FarPlane + NearPlane) / (FarPlane - NearPlane), 1
                );
            }
            
            return Matrix4x4.CreatePerspective(
                FieldOfView * (MathF.PI / 180f), 
                AspectRatio, 
                NearPlane, 
                FarPlane
            );
        }
    }

    /// <summary>
    /// Mesh component for rendering 3D geometry.
    /// </summary>
    public struct MeshComponent
    {
        public int VertexBufferId;
        public int IndexBufferId;
        public int VertexCount;
        public int IndexCount;
        public int MaterialId;
        
        public MeshComponent(int vertexBufferId, int indexBufferId, int vertexCount, int indexCount, int materialId = -1)
        {
            VertexBufferId = vertexBufferId;
            IndexBufferId = indexBufferId;
            VertexCount = vertexCount;
            IndexCount = indexCount;
            MaterialId = materialId;
        }
    }

    /// <summary>
    /// Material component for surface properties.
    /// </summary>
    public struct MaterialComponent
    {
        public Vector3 AlbedoColor;
        public float Metallic;
        public float Roughness;
        public float Emission;
        public int AlbedoTextureId;
        public int NormalTextureId;
        public int MetallicRoughnessTextureId;
        
        public MaterialComponent(Vector3 albedo, float metallic = 0f, float roughness = 0.5f)
        {
            AlbedoColor = albedo;
            Metallic = metallic;
            Roughness = roughness;
            Emission = 0f;
            AlbedoTextureId = -1;
            NormalTextureId = -1;
            MetallicRoughnessTextureId = -1;
        }
        
        public static MaterialComponent Default => new(Vector3.One);
    }

    /// <summary>
    /// Light component for illumination.
    /// </summary>
    public struct LightComponent
    {
        public enum LightType { Directional, Point, Spot }
        
        public LightType Type;
        public Vector3 Color;
        public float Intensity;
        public float Range;
        public float SpotAngle;
        public bool CastsShadows;
        
        public LightComponent(LightType type, Vector3 color, float intensity = 1f)
        {
            Type = type;
            Color = color;
            Intensity = intensity;
            Range = type == LightType.Directional ? float.MaxValue : 10f;
            SpotAngle = 45f;
            CastsShadows = true;
        }
        
        public static LightComponent Directional(Vector3 color, float intensity = 1f) => 
            new(LightType.Directional, color, intensity);
            
        public static LightComponent Point(Vector3 color, float intensity = 1f, float range = 10f) => 
            new(LightType.Point, color, intensity) { Range = range };
    }

    /// <summary>
    /// Velocity component for physics and animation.
    /// </summary>
    public struct VelocityComponent
    {
        public Vector3 Linear;
        public Vector3 Angular;
        
        public VelocityComponent(Vector3 linear, Vector3 angular)
        {
            Linear = linear;
            Angular = angular;
        }
        
        public VelocityComponent(Vector3 linear) : this(linear, Vector3.Zero) { }
        public VelocityComponent() : this(Vector3.Zero, Vector3.Zero) { }
    }
}
