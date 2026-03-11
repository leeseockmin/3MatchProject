using UnityEngine;
using UnityEngine.SceneManagement;

namespace ThreeMatch
{
    public static class Match3Bootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateGame()
        {
            if (Object.FindObjectOfType<Match3Game>() != null)
            {
                return;
            }

            Scene activeScene = SceneManager.GetActiveScene();
            if (!activeScene.IsValid())
            {
                return;
            }

            if (Camera.main == null)
            {
                GameObject cameraObject = new("Main Camera");
                cameraObject.tag = "MainCamera";
                cameraObject.AddComponent<Camera>();
            }

            GameObject root = new("3MatchGame");
            root.AddComponent<Match3Game>();
        }
    }
}
