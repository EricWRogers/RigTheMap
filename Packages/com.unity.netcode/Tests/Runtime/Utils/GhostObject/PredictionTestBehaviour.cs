namespace Unity.Netcode.Tests
{
    internal struct PredictionTestBehaviourTestInput : IInputComponentData
    {
        [GhostField] public int Value;
    }
    internal partial class PredictionTestBehaviour : GhostBehaviour
    {
        GhostComponentRef<PredictionTestBehaviourTestInput> inputRef;
        public int ValueForInput;

        public int PredictedValue;

        public override void PredictionGatherInput(PredictionGatherInputContext context)
        {
            ValueForInput++;
            PredictionTestBehaviourTestInput inputData = default;
            inputData.Value = ValueForInput;
            inputRef.Value = inputData;
        }

        public override void PredictionUpdate(PredictionUpdateContext context)
        {
            PredictedValue = this.inputRef.Value.Value;
        }
    }
}
