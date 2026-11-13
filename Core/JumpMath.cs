namespace JDMemory.Core
{
    /// <summary>
    /// JD（ジャンプ距離）と RT（反応時間）の計算。
    /// ゲーム本体の決め方に合わせている:
    ///   1拍の長さ = 60 / BPM
    ///   ハーフジャンプ拍数 = 4 から始めて「NJS × 1拍 × 拍数」が最大距離(18)を超えなくなるまで半分にする
    ///   その拍数にオフセットを足す（下限 0.25 拍）
    ///   JD = NJS × 1拍 × ハーフジャンプ拍数 × 2
    /// RT(ms) = JD / (2 × NJS) × 1000
    /// </summary>
    internal static class JumpMath
    {
        internal const float DefaultStartHalfJumpBeats = 4f;
        internal const float DefaultMaxHalfJumpDistance = 18f;
        internal const float MinHalfJumpBeats = 0.25f;

        internal static float BaseHalfJumpBeats(float bpm, float njs,
            float startHalfJumpBeats = DefaultStartHalfJumpBeats,
            float maxHalfJumpDistance = DefaultMaxHalfJumpDistance)
        {
            float oneBeat = 60f / bpm;
            float halfJumpBeats = startHalfJumpBeats;
            float limit = maxHalfJumpDistance - 0.001f;
            while (njs * oneBeat * halfJumpBeats > limit && halfJumpBeats > 0.0001f)
            {
                halfJumpBeats /= 2f;
            }
            return halfJumpBeats;
        }

        /// <summary>譜面の BPM・NJS・オフセットから JD を求める</summary>
        internal static float JumpDistance(float bpm, float njs, float offset,
            float startHalfJumpBeats = DefaultStartHalfJumpBeats,
            float maxHalfJumpDistance = DefaultMaxHalfJumpDistance)
        {
            if (bpm <= 0f || njs <= 0.01f)
            {
                return 0f;
            }
            float halfJumpBeats = BaseHalfJumpBeats(bpm, njs, startHalfJumpBeats, maxHalfJumpDistance) + offset;
            if (halfJumpBeats < MinHalfJumpBeats)
            {
                halfJumpBeats = MinHalfJumpBeats;
            }
            return njs * (60f / bpm) * halfJumpBeats * 2f;
        }

        /// <summary>目標の JD にするためのオフセット（拍）を求める</summary>
        internal static float OffsetForJumpDistance(float bpm, float njs, float jumpDistance,
            float startHalfJumpBeats = DefaultStartHalfJumpBeats,
            float maxHalfJumpDistance = DefaultMaxHalfJumpDistance)
        {
            float targetHalfJumpBeats = jumpDistance / (2f * njs * (60f / bpm));
            return targetHalfJumpBeats - BaseHalfJumpBeats(bpm, njs, startHalfJumpBeats, maxHalfJumpDistance);
        }

        /// <summary>その譜面で設定できる最小の JD（ハーフジャンプ 0.25 拍）</summary>
        internal static float MinJumpDistance(float bpm, float njs)
        {
            if (bpm <= 0f || njs <= 0.01f)
            {
                return 0f;
            }
            return njs * (60f / bpm) * MinHalfJumpBeats * 2f;
        }

        internal static float ToReactionTime(float jumpDistance, float njs)
        {
            return njs > 0.01f ? jumpDistance * 500f / njs : 0f;
        }

        internal static float ToJumpDistance(float reactionTimeMs, float njs)
        {
            return reactionTimeMs * njs / 500f;
        }
    }
}
