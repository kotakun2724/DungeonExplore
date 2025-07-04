using UnityEngine;
using System.Collections.Generic;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class DungeonGenerator : MonoBehaviour
{
    [Header("Room Generation Settings")]
    [SerializeField] int roomCount = 10;
    [SerializeField] int maxBranch = 2; // 1コネクタからの最大分岐数
    [SerializeField] float minRoomDistance = 8f;
    [SerializeField] Vector2 areaSize = new Vector2(40, 40); // X: width, Y: height (world units)

    [Header("Prefabs (assign in Inspector)")]
    [SerializeField] GameObject roomPrefab; // Room_A
    [SerializeField] GameObject[] corridorPrefabs; // Floor, Floor_L, Floor_T, etc.
    [SerializeField] GameObject[] wallPrefabs;
    [SerializeField] Transform root;

    [Header("Debug/Generated")]
    [SerializeField, HideInInspector] List<GameObject> spawnedRooms = new();
    [SerializeField, HideInInspector] List<GameObject> spawnedCorridors = new();

    // Inspectorボタン
#if UNITY_EDITOR
    [ContextMenu("Generate Dungeon")]
    public void GenerateDungeon() {
        ClearDungeon();
        GenerateConnectorBasedDungeon();
    }
    [ContextMenu("Clear Dungeon")]
    public void ClearDungeon() {
        foreach (var go in spawnedRooms) if (go) DestroyImmediate(go);
        foreach (var go in spawnedCorridors) if (go) DestroyImmediate(go);
        spawnedRooms.Clear();
        spawnedCorridors.Clear();
        if (root != null) {
            var children = new List<Transform>();
            foreach (Transform t in root) children.Add(t);
            foreach (var t in children) DestroyImmediate(t.gameObject);
        }
    }
#endif

    class ConnectorInfo
    {
        public GameObject owner;
        public Transform connector;
        public string type; // "Male" or "Female"
        public bool used = false;
    }

    void GenerateConnectorBasedDungeon()
    {
        spawnedRooms.Clear();
        spawnedCorridors.Clear();
        // 1. スタート部屋生成
        var startRoom = Instantiate(roomPrefab, Vector3.zero, Quaternion.identity, root);
        spawnedRooms.Add(startRoom);
        // 2. 未接続コネクタリスト作成
        var openConnectors = new List<ConnectorInfo>();
        foreach (var c in FindConnectors(startRoom))
        {
            openConnectors.Add(new ConnectorInfo
            {
                owner = startRoom,
                connector = c,
                type = c.name.Contains("Male") ? "Male" : "Female"
            });
        }
        int placedRooms = 1;
        var rand = new System.Random();
        // 3. コネクタをたどって拡張
        while (placedRooms < roomCount && openConnectors.Count > 0)
        {
            // ランダムな未使用コネクタを選ぶ
            var candidates = openConnectors.Where(c => !c.used).ToList();
            if (candidates.Count == 0) break;
            var baseConnector = candidates[rand.Next(candidates.Count)];
            baseConnector.used = true;
            int branch = 1 + rand.Next(maxBranch); // 1～maxBranch分岐
            for (int b = 0; b < branch && placedRooms < roomCount; b++)
            {
                // 50%で部屋、50%で通路
                bool placeRoom = rand.NextDouble() < 0.5;
                if (placeRoom)
                {
                    // 新しい部屋をランダムな回転で仮生成
                    var newRoom = Instantiate(roomPrefab, Vector3.zero, Quaternion.identity, root);
                    // 新しい部屋の未使用コネクタを取得
                    var newConnectors = FindConnectors(newRoom).Where(c =>
                        (baseConnector.type == "Male" && c.name.Contains("Female")) ||
                        (baseConnector.type == "Female" && c.name.Contains("Male"))
                    ).ToList();
                    if (newConnectors.Count == 0) { DestroyImmediate(newRoom); continue; }
                    var newConn = newConnectors[rand.Next(newConnectors.Count)];
                    // 新しい部屋のコネクタをbaseConnectorに合わせて位置・回転を調整
                    AlignConnector(newRoom, newConn, baseConnector.connector);
                    // 距離チェック
                    if (IsRoomPositionValid(newRoom.transform.position))
                    {
                        spawnedRooms.Add(newRoom);
                        placedRooms++;
                        // 新しい部屋の他のコネクタをopenに追加
                        foreach (var c in FindConnectors(newRoom))
                        {
                            if (c == newConn) continue;
                            openConnectors.Add(new ConnectorInfo
                            {
                                owner = newRoom,
                                connector = c,
                                type = c.name.Contains("Male") ? "Male" : "Female"
                            });
                        }
                    }
                    else
                    {
                        DestroyImmediate(newRoom);
                    }
                }
                else
                {
                    // 通路Prefabをランダム選択
                    var prefab = corridorPrefabs[rand.Next(corridorPrefabs.Length)];
                    var newCorridor = Instantiate(prefab, Vector3.zero, Quaternion.identity, root);
                    var newConnectors = FindConnectors(newCorridor).Where(c =>
                        (baseConnector.type == "Male" && c.name.Contains("Female")) ||
                        (baseConnector.type == "Female" && c.name.Contains("Male"))
                    ).ToList();
                    if (newConnectors.Count == 0) { DestroyImmediate(newCorridor); continue; }
                    var newConn = newConnectors[rand.Next(newConnectors.Count)];
                    AlignConnector(newCorridor, newConn, baseConnector.connector);
                    spawnedCorridors.Add(newCorridor);
                    // 新しい通路の他のコネクタをopenに追加
                    foreach (var c in FindConnectors(newCorridor))
                    {
                        if (c == newConn) continue;
                        openConnectors.Add(new ConnectorInfo
                        {
                            owner = newCorridor,
                            connector = c,
                            type = c.name.Contains("Male") ? "Male" : "Female"
                        });
                    }
                }
            }
        }
    }

    // コネクタa（新規）をb（既存）にピッタリ合わせる
    void AlignConnector(GameObject obj, Transform a, Transform b)
    {
        // 1. 回転を合わせる
        var rotTo = Quaternion.LookRotation(b.forward, b.up) * Quaternion.Inverse(Quaternion.LookRotation(a.forward, a.up));
        obj.transform.rotation = rotTo * obj.transform.rotation;
        // 2. 位置を合わせる
        Vector3 offset = a.position - obj.transform.position;
        obj.transform.position = b.position - offset;
    }

    // コネクタ（"Male"/"Female"を含む子オブジェクト）を取得
    List<Transform> FindConnectors(GameObject go)
    {
        var list = new List<Transform>();
        foreach (Transform t in go.GetComponentsInChildren<Transform>())
        {
            if (t == go.transform) continue;
            if (t.name.Contains("Male") || t.name.Contains("Female")) list.Add(t);
        }
        return list;
    }

    bool IsRoomPositionValid(Vector3 pos)
    {
        foreach (var room in spawnedRooms)
        {
            if (room == null) continue;
            float dist = Vector3.Distance(room.transform.position, pos);
            if (dist < minRoomDistance) return false;
        }
        // エリア範囲チェック
        if (Mathf.Abs(pos.x) > areaSize.x / 2f || Mathf.Abs(pos.z) > areaSize.y / 2f) return false;
        return true;
    }
}

#if UNITY_EDITOR
[CustomEditor(typeof(DungeonGenerator))]
public class DungeonGeneratorEditor : Editor {
    public override void OnInspectorGUI() {
        DrawDefaultInspector();
        DungeonGenerator gen = (DungeonGenerator)target;
        GUILayout.Space(10);
        if (GUILayout.Button("生成 (Generate Dungeon)")) {
            gen.GenerateDungeon();
        }
        if (GUILayout.Button("一括消去 (Clear Dungeon)")) {
            gen.ClearDungeon();
        }
    }
}
#endif
