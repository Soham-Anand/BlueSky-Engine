namespace BlueSky.SkyCore.Builtin
{
    /// <summary>
    /// Marks an entity as an internal editor preview, gizmo, or transient helper
    /// that must never be serialized into scene files or streamed by WorldStreamer.
    /// Replaces the legacy magic ID >= 200 hack.
    /// </summary>
    public struct EditorOnlyTagComponent
    {
        public const uint EditorSystemIdBase = 0xFFFFFF00u;

        public byte Flags;

        public EditorOnlyTagComponent(byte flags = 1)
        {
            Flags = flags;
        }
    }
}
