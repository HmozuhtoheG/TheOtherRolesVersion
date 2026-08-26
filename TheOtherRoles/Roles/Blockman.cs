using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TheOtherRoles.Objects;
using UnityEngine;
using TMPro;
using static TheOtherRoles.TheOtherRoles;

namespace TheOtherRoles.Roles
{
    [TORRPCHolder]
    public class Blockman : RoleBase<Blockman>
    {
        public static Color color = new Color32(255, 165, 0, byte.MaxValue);

        public Blockman()
        {
            RoleId = roleId = RoleId.Blockman;
            currentEnergy = maxEnergy;
            blocks = new();
            triggerBlockmanWin = false;
            AssignBlueprint();
            isPreviewing = false;
            previewLine = null;
            previewBlockGhost = null;
            ghostBlocks = new();
            mapIndicator = null;
            lastAimDirection = Vector2.right;
            dashKeyGuideInitialized = false;
        }

        public static float maxEnergy = 100f;
        public static float energyRegenPerSecond = 3f;
        public static float placeCost = 25f;
        public static float removeRefund = 10f;
        public static int maxBlocks = 5;
        public static float blockLifetime = 45f;
        public static float settleTime = 1f;
        public static float settleTimeSeconds => settleTime * 60f;
        public static float placeRange = 1.5f;

        public static float dashCost = 40f;
        public static float dashDistance = 3f;
        public static float dashCooldown = 15f;
        public static float ghostRevealRange = 6f;
        public const float breakRange = 1.8f;

        public float currentEnergy;
        public List<Block> blocks;
        public bool triggerBlockmanWin;
        public Blueprint blueprint;
        public bool isPreviewing;
        private GameObject previewLine;
        private GameObject previewBlockGhost;
        private List<GameObject> ghostBlocks;
        private GameObject mapIndicator;
        private bool ghostsVisible = false;
        private Vector2 lastAimDirection = Vector2.right;

        private static bool dashKeyGuideInitialized = false;


        public class Block
        {
            public int id;
            public Vector2 position;
            public float age;
            public float placedAtRealtime;
            public bool settled => Time.time - placedAtRealtime >= settleTimeSeconds;
            public GameObject gameObject;
            public TextMeshPro timerText;
        }

        public class Blueprint
        {
            public Vector2Int[] cells;
            public Vector2 originWorldPos;
            public float gridSize = 1f;

            public Vector2 CellWorldPos(Vector2Int cell, Vector2 origin) => origin + new Vector2(cell.x, cell.y) * gridSize;
        }

        private static readonly Vector2Int[][] BlueprintTemplates = new[]
        {
            new[] { new Vector2Int(0,0), new Vector2Int(1,0), new Vector2Int(0,1), new Vector2Int(1,1) },
            new[] { new Vector2Int(0,0), new Vector2Int(1,0), new Vector2Int(2,0) },
            new[] { new Vector2Int(0,0), new Vector2Int(1,0), new Vector2Int(0,1) },
        };

        private static readonly Vector2[] MapCorners = new[]
        {
            new Vector2(-10f, 5f),
            new Vector2(10f, 5f),
            new Vector2(-10f, -5f),
            new Vector2(10f, -5f),
            new Vector2(0f, 8f),
        };

        private void AssignBlueprint()
        {
            var template = BlueprintTemplates[UnityEngine.Random.Range(0, BlueprintTemplates.Length)];
            blueprint = new Blueprint
            {
                cells = template,
                originWorldPos = Vector2.zero
            };
        }

        public override void OnFinishShipStatusBegin()
        {
            if (player != PlayerControl.LocalPlayer) return;

            // pick a random map corner and jitter around it until the whole blueprint fits;
            // if none of the corners work, just jitter around the player as a last resort
            Vector2 chosenOrigin = (Vector2)player.transform.position;
            bool found = false;

            foreach (var corner in MapCorners.OrderBy(c => UnityEngine.Random.value))
            {
                if (!TryFindFittingOrigin(corner, 3f, 10, out var candidate)) continue;
                chosenOrigin = candidate;
                found = true;
                break;
            }

            if (!found && TryFindFittingOrigin((Vector2)player.transform.position, 4f, 20, out var fallback))
                chosenOrigin = fallback;

            blueprint.originWorldPos = chosenOrigin;
            CreateGhostBlueprint();
            CreateMapIndicator();
        }

        private bool TryFindFittingOrigin(Vector2 basePos, float jitterRadius, int attempts, out Vector2 origin)
        {
            var currentData = MapData.GetCurrentMapData();
            origin = basePos;

            for (int attempt = 0; attempt < attempts; attempt++)
            {
                Vector2 candidate = basePos + UnityEngine.Random.insideUnitCircle * jitterRadius;
                bool fits = true;
                foreach (var cell in blueprint.cells)
                {
                    Vector2 cellPos = blueprint.CellWorldPos(cell, candidate);
                    if (!currentData.CheckMapArea(cellPos, 0.25f)) { fits = false; break; }
                }
                if (fits)
                {
                    origin = candidate;
                    return true;
                }
            }
            return false;
        }

        private static Sprite buttonSprite;
        public static Sprite getButtonSprite()
        {
            if (buttonSprite) return buttonSprite;
            buttonSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.BlockPlaceButton.png", 115f);
            return buttonSprite;
        }

        private static Sprite dashButtonSprite;
        public static Sprite getDashButtonSprite()
        {
            if (dashButtonSprite) return dashButtonSprite;
            dashButtonSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.BlockDashButton.png", 115f);
            return dashButtonSprite;
        }

        private static Sprite blockSprite;
        public static Sprite getBlockSprite()
        {
            if (blockSprite) return blockSprite;
            blockSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.BlockObject.png", 100f);
            return blockSprite;
        }

        private static Sprite breakButtonSprite;
        public static Sprite getBreakButtonSprite()
        {
            if (breakButtonSprite) return breakButtonSprite;
            breakButtonSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.BlockBreakButton.png", 115f);
            return breakButtonSprite;
        }

        public static RemoteProcess<(byte playerId, int blockId, Vector2 pos)> PlaceBlock = new("BlockmanPlace", (message, _) =>
        {
            var role = getRole(Helpers.playerById(message.playerId));
            if (role == null) return;
            if (role.blocks.Count >= maxBlocks) return;

            Block block = new() { id = message.blockId, position = message.pos, age = 0f, placedAtRealtime = Time.time };
            block.gameObject = CreateBlockGameObject(message.pos, block);
            role.blocks.Add(block);
        });

        public static RemoteProcess<(byte playerId, int blockId, byte refund)> RemoveBlock = new("BlockmanRemove", (message, _) =>
        {
            var role = getRole(Helpers.playerById(message.playerId));
            if (role == null) return;
            var block = role.blocks.FirstOrDefault(b => b.id == message.blockId);
            if (block == null) return;

            if (block.gameObject != null) UnityEngine.Object.Destroy(block.gameObject);
            role.blocks.Remove(block);

            if (message.refund != 0 && PlayerControl.LocalPlayer == role.player)
                role.currentEnergy = Mathf.Min(maxEnergy, role.currentEnergy + removeRefund);
        });

        private static GameObject CreateBlockGameObject(Vector2 pos, Block blockRef)
        {
            var obj = new GameObject("BlockmanBlock");
            obj.transform.position = new Vector3(pos.x, pos.y, pos.y / 1000f);

            var renderer = obj.AddComponent<SpriteRenderer>();
            renderer.sprite = getBlockSprite();

            var textObj = new GameObject("BlockTimerText");
            textObj.transform.SetParent(obj.transform);
            textObj.transform.localPosition = new Vector3(0f, 0.8f, 0f);
            var textMesh = textObj.AddComponent<TextMeshPro>();
            textMesh.text = "0.0s";
            textMesh.fontSize = 3f;
            textMesh.alignment = TextAlignmentOptions.Center;
            textMesh.color = Color.white;
            blockRef.timerText = textMesh;

            var collider = obj.AddComponent<BoxCollider2D>();
            collider.isTrigger = false;
            collider.size = Vector2.one * 0.9f;

            return obj;
        }

        private int nextLocalBlockId = 0;

        private Vector2 GetMousePlacePos()
        {
            if (player == null) return Vector2.zero;
            Vector2 direction = GetAimDirection();
            return (Vector2)player.transform.position + direction * placeRange;
        }

        private Vector2 GetAimDirection()
        {
            if (System.OperatingSystem.IsAndroid())
            {
                Vector2 velocity = player.MyPhysics.body.velocity;
                if (velocity.sqrMagnitude > 0.0001f) lastAimDirection = velocity.normalized;
                return lastAimDirection;
            }

            Vector3 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            mouseWorld.z = 0f;
            Vector2 direction = (Vector2)mouseWorld - (Vector2)player.transform.position;
            return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.right;
        }

        public void TogglePlaceBlock()
        {
            if (player != PlayerControl.LocalPlayer) return;

            if (!isPreviewing)
            {
                isPreviewing = true;
                ShowPreview();
            }
            else
            {
                isPreviewing = false;
                TryPlaceBlock();
                ClearPreview();
            }
        }

        private void TryPlaceBlock()
        {
            if (player != PlayerControl.LocalPlayer) return;

            if (currentEnergy < placeCost)
            {
                Helpers.CreateAndShowNotification(ModTranslation.getString("BlockmanNotEnoughEnergy"), Color.white, new Vector3(0f, 1f, -20f));
                return;
            }
            if (blocks.Count >= maxBlocks)
            {
                Helpers.CreateAndShowNotification(ModTranslation.getString("BlockmanMaxBlocks"), Color.white, new Vector3(0f, 1f, -20f));
                return;
            }

            Vector2 placePos = GetMousePlacePos();
            currentEnergy -= placeCost;
            int id = nextLocalBlockId++;
            PlaceBlock.Invoke((player.PlayerId, id, placePos));
        }

        public void TryRemoveOwnBlock(int blockId)
        {
            if (player != PlayerControl.LocalPlayer) return;
            RemoveBlock.Invoke((player.PlayerId, blockId, 1));
        }

        public static void TryUniversalBreak()
        {
            var player = PlayerControl.LocalPlayer;
            if (player == null || player.Data.IsDead) return;

            var (owner, block) = FindNearestUnsettledBlock(player.transform.position, breakRange);

            if (owner != null && block != null)
                BreakUnsettledBlock(owner, block);
            else
                Helpers.CreateAndShowNotification(ModTranslation.getString("BlockmanNoUnsettledBlock"), Color.white, new Vector3(0f, 1f, -20f));
        }

        public static void BreakUnsettledBlock(Blockman owner, Block block)
        {
            if (owner == null || block == null || block.settled) return;
            RemoveBlock.Invoke((owner.player.PlayerId, block.id, 0));
        }

        public static (Blockman owner, Block block) FindNearestUnsettledBlock(Vector2 fromPos, float maxDistance)
        {
            Blockman bestOwner = null;
            Block bestBlock = null;
            float bestDist = maxDistance;

            foreach (var bm in players)
            {
                if (bm.blocks == null) continue;
                foreach (var block in bm.blocks)
                {
                    if (block.settled) continue;
                    float dist = Vector2.Distance(fromPos, block.position);
                    if (dist <= bestDist)
                    {
                        bestDist = dist;
                        bestOwner = bm;
                        bestBlock = block;
                    }
                }
            }
            return (bestOwner, bestBlock);
        }

        public static RemoteProcess<(byte playerId, Vector2 targetPos)> Dash = new("BlockmanDash", (message, _) =>
        {
            var role = getRole(Helpers.playerById(message.playerId));
            if (role == null || role.player == null) return;
            role.player.NetTransform.RpcSnapTo(message.targetPos);
        });

        public static RemoteProcess<byte> TriggerWin = RemotePrimitiveProcess.OfByte("BlockmanWin", (message, _) =>
        {
            var role = getRole(Helpers.playerById(message));
            if (role == null) return;
            role.triggerBlockmanWin = true;
        });

        public void TryDash()
        {
            if (player != PlayerControl.LocalPlayer) return;
            if (currentEnergy < dashCost) return;

            Vector2 direction = GetAimDirection();
            Vector2 target = (Vector2)player.transform.position + direction * dashDistance;

            if (!MapData.GetCurrentMapData().CheckMapArea(target, 0.25f))
            {
                Helpers.CreateAndShowNotification(ModTranslation.getString("BlockmanDashInvalid"), Color.white, new Vector3(0f, 1f, -20f));
                return;
            }

            currentEnergy -= dashCost;
            Dash.Invoke((player.PlayerId, target));
        }

        private void ShowPreview()
        {
            if (player == null) return;

            Vector2 placePos = GetMousePlacePos();

            if (previewLine == null)
            {
                previewLine = new GameObject("PreviewLine");
                var line = previewLine.AddComponent<LineRenderer>();
                line.material = new Material(Shader.Find("Sprites/Default"));
                line.startColor = Color.green;
                line.endColor = Color.green;
                line.startWidth = 0.1f;
                line.endWidth = 0.1f;
                line.positionCount = 2;
            }
            var lineRenderer = previewLine.GetComponent<LineRenderer>();
            Vector3 start = player.transform.position + Vector3.up * 0.1f;
            Vector3 end = new(placePos.x, placePos.y, placePos.y / 1000f + 0.1f);
            lineRenderer.SetPosition(0, start);
            lineRenderer.SetPosition(1, end);

            if (previewBlockGhost == null)
            {
                previewBlockGhost = new GameObject("PreviewGhost");
                var sr = previewBlockGhost.AddComponent<SpriteRenderer>();
                sr.sprite = getBlockSprite();
                sr.color = new Color(1f, 1f, 1f, 0.5f);
                previewBlockGhost.transform.position = new Vector3(placePos.x, placePos.y, placePos.y / 1000f + 0.05f);
            }
            else
            {
                previewBlockGhost.transform.position = new Vector3(placePos.x, placePos.y, placePos.y / 1000f + 0.05f);
            }
        }

        private void ClearPreview()
        {
            if (previewLine != null)
            {
                UnityEngine.Object.Destroy(previewLine);
                previewLine = null;
            }
            if (previewBlockGhost != null)
            {
                UnityEngine.Object.Destroy(previewBlockGhost);
                previewBlockGhost = null;
            }
        }

        private void UpdatePreview()
        {
            if (!isPreviewing || player == null) return;

            Vector2 placePos = GetMousePlacePos();

            if (previewLine != null)
            {
                var line = previewLine.GetComponent<LineRenderer>();
                if (line != null)
                {
                    Vector3 start = player.transform.position + Vector3.up * 0.1f;
                    Vector3 end = new(placePos.x, placePos.y, placePos.y / 1000f + 0.1f);
                    line.SetPosition(0, start);
                    line.SetPosition(1, end);
                }
            }
            if (previewBlockGhost != null)
            {
                previewBlockGhost.transform.position = new Vector3(placePos.x, placePos.y, placePos.y / 1000f + 0.05f);
            }
        }

        private void CreateGhostBlueprint()
        {
            if (player != PlayerControl.LocalPlayer) return;
            if (blueprint.originWorldPos == Vector2.zero) return;

            foreach (var cell in blueprint.cells)
            {
                Vector2 worldPos = blueprint.CellWorldPos(cell, blueprint.originWorldPos);
                GameObject ghost = new("BlueprintGhost");
                ghost.transform.position = new Vector3(worldPos.x, worldPos.y, 4f);
                var sr = ghost.AddComponent<SpriteRenderer>();
                sr.sprite = getBlockSprite();
                sr.color = new Color(0f, 1f, 0f, 0.35f);
                sr.sortingOrder = 50;
                ghost.SetActive(false);
                ghostBlocks.Add(ghost);
            }
            ghostsVisible = false;
        }

        private void ClearGhosts()
        {
            foreach (var g in ghostBlocks)
            {
                if (g != null) UnityEngine.Object.Destroy(g);
            }
            ghostBlocks.Clear();
            if (mapIndicator != null)
            {
                UnityEngine.Object.Destroy(mapIndicator);
                mapIndicator = null;
            }
        }

        private void CreateMapIndicator()
        {
            if (player != PlayerControl.LocalPlayer) return;
            if (blueprint.originWorldPos == Vector2.zero) return;

            mapIndicator = new GameObject("BlueprintMapIndicator");
            var sr = mapIndicator.AddComponent<SpriteRenderer>();
            sr.sprite = getBlockSprite();
            sr.color = new Color(1f, 1f, 0f, 0.8f);
            mapIndicator.transform.position = new Vector3(blueprint.originWorldPos.x, blueprint.originWorldPos.y, 5f);
            mapIndicator.transform.localScale = Vector3.one * 0.5f;
            sr.sortingOrder = 100;
        }

        private void UpdateGhostVisibility()
        {
            if (player != PlayerControl.LocalPlayer || ghostBlocks == null || ghostBlocks.Count == 0) return;

            bool shouldShow = false;
            Vector2 playerPos = player.transform.position;
            foreach (var cell in blueprint.cells)
            {
                Vector2 cellPos = blueprint.CellWorldPos(cell, blueprint.originWorldPos);
                if (Vector2.Distance(playerPos, cellPos) <= ghostRevealRange)
                {
                    shouldShow = true;
                    break;
                }
            }

            if (shouldShow != ghostsVisible)
            {
                ghostsVisible = shouldShow;
                foreach (var g in ghostBlocks)
                {
                    if (g != null) g.SetActive(ghostsVisible);
                }
            }

            if (mapIndicator != null)
            {
                float pulse = 0.5f + 0.3f * Mathf.Sin(Time.time * 2f);
                var sr = mapIndicator.GetComponent<SpriteRenderer>();
                if (sr != null) sr.color = new Color(1f, 1f, 0f, pulse);
            }
        }

        private static void UpdateAllBlockTexts()
        {
            foreach (var bm in players)
            {
                if (bm.blocks == null) continue;
                foreach (var block in bm.blocks)
                {
                    if (block.gameObject == null || block.timerText == null) continue;
                    float remaining = Mathf.Max(0f, settleTimeSeconds - (Time.time - block.placedAtRealtime));
                    block.timerText.text = remaining.ToString("F1") + "s";
                    if (Camera.main != null)
                        block.timerText.transform.rotation = Quaternion.LookRotation(Camera.main.transform.forward);
                }
            }
        }

        public static void UpdateEnergyText(TMPro.TMP_Text text)
        {
            if (text == null) return;
            var role = local;
            if (role != null)
                text.text = $"{Mathf.FloorToInt(role.currentEnergy)}/{Mathf.FloorToInt(maxEnergy)}";
            else
                text.text = "";
        }

        public override void FixedUpdate()
        {
            if (player != PlayerControl.LocalPlayer) return;

            if (!player.Data.IsDead)
                currentEnergy = Mathf.Min(maxEnergy, currentEnergy + energyRegenPerSecond * Time.fixedDeltaTime);

            List<Block> expired = null;
            foreach (var block in blocks)
            {
                block.age += Time.fixedDeltaTime;
                if (block.age >= blockLifetime) (expired ??= new List<Block>()).Add(block);
            }
            if (expired != null)
                foreach (var block in expired)
                    RemoveBlock.Invoke((player.PlayerId, block.id, 0));

            CheckWinCondition();

            UpdatePreview();
            UpdateAllBlockTexts();
            UpdateGhostVisibility();

            if (HudManagerStartPatch.blockmanEnergyText != null)
                UpdateEnergyText(HudManagerStartPatch.blockmanEnergyText);

            // 参考原版击杀键显示逻辑：游戏内改键后立即同步方块人冲刺按钮的绑定与显示
            var blockmanKeyboardMap = Rewired.ReInput.mapping.GetKeyboardMapInstanceSavedOrDefault(0, 0, 0);
            var blockmanKillMaps = blockmanKeyboardMap.GetButtonMapsWithAction(8);
            if (blockmanKillMaps.Count > 0 && HudManagerStartPatch.blockmanDashButton != null)
            {
                var blockmanKillKey = (UnityEngine.KeyCode)blockmanKillMaps[0].keyCode;
                if (!dashKeyGuideInitialized || HudManagerStartPatch.blockmanDashButton.hotkey != blockmanKillKey)
                {
                    HudManagerStartPatch.blockmanDashButton.hotkey = blockmanKillKey;
                    HudManagerStartPatch.blockmanDashButton.setKeyBind();
                    dashKeyGuideInitialized = true;
                }
            }
        }

        private void CheckWinCondition()
        {
            if (!triggerBlockmanWin && IsBlueprintComplete(this))
                TriggerWin.Invoke(player.PlayerId);
        }

        public static bool IsBlueprintComplete(Blockman role)
        {
            if (role == null || role.player == null || role.player.Data.IsDead) return false;
            if (role.blueprint == null || role.blueprint.originWorldPos == Vector2.zero) return false;

            foreach (var cell in role.blueprint.cells)
            {
                Vector2 targetPos = role.blueprint.CellWorldPos(cell, role.blueprint.originWorldPos);
                bool filled = role.blocks.Any(b => b.settled && Vector2.Distance(b.position, targetPos) < 0.5f);
                if (!filled) return false;
            }

            return true;
        }

        public override void OnMeetingStart()
        {
        }

        public override void OnDeath(PlayerControl killer = null)
        {
            ClearGhosts();
            ClearPreview();
        }

        public static void clearAndReload()
        {
            if (players != null) players.Do(x => x.triggerBlockmanWin = false);
            maxEnergy = CustomOptionHolder.blockmanMaxEnergy.getFloat();
            energyRegenPerSecond = CustomOptionHolder.blockmanEnergyRegenRate.getFloat();
            placeCost = CustomOptionHolder.blockmanPlaceCost.getFloat();
            removeRefund = CustomOptionHolder.blockmanRemoveRefund.getFloat();
            maxBlocks = Mathf.RoundToInt(CustomOptionHolder.blockmanMaxBlocks.getFloat());
            blockLifetime = CustomOptionHolder.blockmanBlockLifetime.getFloat();
            settleTime = CustomOptionHolder.blockmanSettleTime.getFloat();
            dashCost = CustomOptionHolder.blockmanDashCost.getFloat();
            dashDistance = CustomOptionHolder.blockmanDashDistance.getFloat();
            dashCooldown = CustomOptionHolder.blockmanDashCooldown.getFloat();
            players = [];
        }
    }
}
