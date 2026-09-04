using System.Collections.Generic;
using UnityEngine;

namespace HalMenu.Core
{
    [CreateAssetMenu(fileName = "GameCatalog", menuName = "HalMenu/Game Catalog")]
    public class GameCatalog : ScriptableObject
    {
        public List<GameManifest> games = new List<GameManifest>();
    }
}
