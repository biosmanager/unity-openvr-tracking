using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OVRT
{
    public class OVRT_FrameInfo
    {
        public bool IsHighResolutionTimer { get; set; }

        public int FrameCount { get; set; }
        public ulong FrameCountInternal { get; set; }
        public bool IsValidPrediction { get; set; }
        public double LastVsyncTimestampSeconds { get; set; }
        public double SecondsSinceLastVsync { get; set; }
        public double PosePredictionTimestampSeconds { get; set; }
        public double PredictedSeconds { get; set; }
        public float FrameDurationSeconds { get; set; }
    }
}
