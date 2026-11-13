using System;
using JDMemory.Core;
using Zenject;

namespace JDMemory.Managers
{
    /// <summary>ソロの曲選択画面で選ばれている譜面を見張る</summary>
    internal sealed class SelectionWatcher : IInitializable, IDisposable
    {
        private readonly StandardLevelDetailViewController _levelDetail;

        public SelectionWatcher(StandardLevelDetailViewController levelDetail)
        {
            _levelDetail = levelDetail;
        }

        public void Initialize()
        {
            _levelDetail.didChangeDifficultyBeatmapEvent += OnDifficultyChanged;
            _levelDetail.didChangeContentEvent += OnContentChanged;
        }

        public void Dispose()
        {
            _levelDetail.didChangeDifficultyBeatmapEvent -= OnDifficultyChanged;
            _levelDetail.didChangeContentEvent -= OnContentChanged;
        }

        private void OnDifficultyChanged(StandardLevelDetailViewController controller)
        {
            Update(controller);
        }

        private void OnContentChanged(StandardLevelDetailViewController controller, StandardLevelDetailViewController.ContentType contentType)
        {
            Update(controller);
        }

        private static void Update(StandardLevelDetailViewController controller)
        {
            try
            {
                if (controller == null || controller.beatmapLevel == null)
                {
                    return;
                }
                PlayState.SetSelected(MapSelection.Create(controller.beatmapKey, controller.beatmapLevel));
            }
            catch (Exception e)
            {
                Plugin.Log.Error(e);
            }
        }
    }
}
