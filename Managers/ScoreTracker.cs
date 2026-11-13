using System;
using JDMemory.Core;
using Zenject;

namespace JDMemory.Managers
{
    /// <summary>プレイ中のスコアと「その時点での最大スコア」を追いかけて、精度を出せるようにする</summary>
    internal sealed class ScoreTracker : IInitializable, IDisposable
    {
        private readonly IScoreController _scoreController;

        // 万一取得できなくてもプレイを止めないよう、任意の注入にしておく
        public ScoreTracker([InjectOptional] IScoreController scoreController)
        {
            _scoreController = scoreController;
        }

        public void Initialize()
        {
            if (_scoreController == null)
            {
                Plugin.Log.Warn("IScoreController が見つかりません。精度は記録されません");
                return;
            }
            _scoreController.scoreDidChangeEvent += OnScoreChanged;
        }

        public void Dispose()
        {
            if (_scoreController != null)
            {
                _scoreController.scoreDidChangeEvent -= OnScoreChanged;
            }
        }

        private void OnScoreChanged(int multipliedScore, int modifiedScore)
        {
            PlayState.OnScoreChanged(_scoreController.multipliedScore, _scoreController.immediateMaxPossibleMultipliedScore);
        }
    }
}
