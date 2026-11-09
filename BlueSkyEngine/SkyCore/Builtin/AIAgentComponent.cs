using System;

namespace BlueSky.SkyCore.Builtin
{
    /// <summary>
    /// SkyCore AI agent component backed by the Overthinking AI subsystem.
    /// </summary>
    public unsafe struct AIAgentComponent
    {
        private const int NameCapacity = 128;
        private fixed char _behaviorProfile[NameCapacity];

        public float PerceptionRadius;
        public float DecisionInterval;
        public float DecisionTimer;
        public bool IsEnabled;
        public int ActiveGoalPriority;

        public string BehaviorProfile
        {
            get
            {
                fixed (char* ptr = _behaviorProfile)
                {
                    int len = 0;
                    while (len < NameCapacity && ptr[len] != '\0') len++;
                    return len == 0 ? string.Empty : new string(ptr, 0, len);
                }
            }
            set
            {
                value ??= string.Empty;
                int len = System.Math.Min(NameCapacity - 1, value.Length);
                fixed (char* ptr = _behaviorProfile)
                {
                    for (int i = 0; i < len; i++) ptr[i] = value[i];
                    ptr[len] = '\0';
                }
            }
        }

        public AIAgentComponent(string behaviorProfile = "DefaultAgent", float perceptionRadius = 25.0f, float decisionInterval = 0.2f)
        {
            _behaviorProfile[0] = '\0';
            PerceptionRadius = perceptionRadius;
            DecisionInterval = decisionInterval;
            DecisionTimer = 0f;
            IsEnabled = true;
            ActiveGoalPriority = 0;
            BehaviorProfile = behaviorProfile;
        }
    }

}
