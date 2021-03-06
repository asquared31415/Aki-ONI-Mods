﻿using LibNoiseDotNet.Graphics.Tools.Noise;
using LibNoiseDotNet.Graphics.Tools.Noise.Filter;
using LibNoiseDotNet.Graphics.Tools.Noise.Primitive;
using ProcGen;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace WorldTraitsPlus.WorldEvents
{
    public class SeismicGrid
    {
        private const float GEYSER_SPAWN_TRESHOLD = 0.8f;
        private const int CHUNK_EDGE = 8;
        private static Element neutronium;

        public static float[] activity;
        public static Dictionary<int, float> chunks;
        public static float highestActivity = 0;

        private static int selectedChunk = -1;
        private static int selectedCell = -1;
        private static int widthInChunks;
        private static int heightInChunks;

        public static void Initialize()
        {
            neutronium = ElementLoader.FindElementByHash(SimHashes.Unobtanium);
            GenerateNoiseMap();
            CreateSafeZones();
            SetChunks();
        }

        private static void GenerateNoiseMap()
        {
            SimplexPerlin perlin = new SimplexPerlin
            {
                Quality = NoiseQuality.Standard,
                Seed = 0 // change later
            };

            RidgedMultiFractal noise = new RidgedMultiFractal
            {
                Frequency = 10,
                Lacunarity = 2,
                OctaveCount = 20,
                Offset = 1,
                Gain = 0,
                Primitive3D = perlin,
                Primitive2D = perlin
            };

            activity = new float[Grid.CellCount];

            // give every cell their activity value
            for (int x = 0; x < Grid.WidthInCells; x++)
            {
                for (int y = 0; y < Grid.HeightInCells; y++)
                {
                    int cell = Grid.XYToCell(x, y);
                    bool spaceZone = Game.Instance.world.zoneRenderData.GetSubWorldZoneType(cell) == SubWorld.ZoneType.Space;

                    if (spaceZone)
                    {
                        activity[cell] = 0;
                    }
                    else
                    {
                        float scale = 1000f;
                        activity[cell] = noise.GetValue(x / scale, y / scale) * (Grid.HeightInCells - y) / Grid.HeightInCells;
                    }
                }
            }
        }

        public static int GetRandomCellInCircle(int center, int r, List<int> cells = null)
        {
            int targetDistance = Mathf.FloorToInt(GetClampedGaussian(0, r));
            int attempt = 0;

            while (attempt++ < 200)
            {
                Vector3 chosenCell = Random.insideUnitCircle * targetDistance;
                int cell = Grid.PosToCell(chosenCell + Grid.CellToPos(center));
                if (Grid.IsValidCell(cell) && (cells == null || cells.Contains(cell)))
                {
                    return cell;
                }
            }

            return -1;
        }

        private static bool CanSpawnGeyser(int cell)
        {
            bool isNeutronium = Grid.Element[cell] == neutronium; ;
            bool isSpace = Game.Instance.world.zoneRenderData.GetSubWorldZoneType(cell) == SubWorld.ZoneType.Space;
            bool isActiveEnough = activity[cell] >= GEYSER_SPAWN_TRESHOLD;

            return !isNeutronium && !isSpace && isActiveEnough;
        }

        private static float GetClampedGaussian(float min, float max)
        {
            return Mathf.Min(Mathf.Max(Util.GaussianRandom(max - min, 1f), min), max);
        }

        private static bool IsProtected(Pickupable go)
        {
            return go.GetComponent<Geyser>() != null;
        }

        private static int GetHighMagnitudeEpicenter(float power)
        {
            var geyserLocations = Components.Pickupables.Items
                .Where(p => !IsProtected(p))
                .Select(p => p.GetCell());

            var eligibleChunks = chunks.Where(c => c.Value >= power);
            List<int> cells = new List<int>();

            foreach (var chunk in eligibleChunks)
            {
                ChunkOffset(chunk.Key, out int cx, out int cy);
                for (int x = 0; x < CHUNK_EDGE; x++)
                {
                    for (int y = 0; y < CHUNK_EDGE; y++)
                    {
                        int cell = Grid.XYToCell(cx + x, cy + y);
                        if (CanSpawnGeyser(cell))
                            cells.Add(cell);
                    }
                }
            }

            foreach (var geyser in geyserLocations)
            {
                Extents extents = new Extents(geyser, 5);
                cells.RemoveAll(c => extents.Contains(Grid.CellToXY(c)));
            }

            // TODO: Check for protected buildings too

            return cells.Count > 0 ? cells.GetRandom() : -1;

        }

        public static List<int> GetBlob(int center, float radius)
        {
            return ProcGen.Util.GetFilledCircle(Grid.CellToPos(center), radius)
                .Select(v => Grid.PosToCell(v))
                .ToList();
        }

        public static Dictionary<int, float> GetCircle(WorldEvent ev, int closeRange = 10, int falloffRange = 5)
        {
            Dictionary<int, float> cells = new Dictionary<int, float>();
            AddCellRange(cells, ev.transform.position, ev.power, closeRange, falloffRange);

            return cells;
        }

        private static void AddCellRange(Dictionary<int, float> cells, Vector3 position, float power, int innerRadius, int outerRadius)
        {
            int radius = innerRadius + outerRadius;
            var range = ProcGen.Util.GetFilledCircle(position, radius);

            foreach (Vector2I pos in range)
            {
                int cell = Grid.XYToCell(Grid.ClampX(pos.x), pos.y);
                if(Grid.IsValidBuildingCell(cell))
                    cells[cell] = GetPower(position, power, innerRadius, outerRadius, pos);
            }
        }

        private static float GetPower(Vector3 position, float power, int innerRadius, int outerRadius, Vector2I pos)
        {
            float distance = Vector2.Distance(position, pos);
            float powerMultiplier = distance > innerRadius ? 1 - (distance - innerRadius) / outerRadius : 1f;
            return power * powerMultiplier;
        }


        public static bool FindAppropiateEpicenter(float power, float highManitudeTreshold, out int cell)
        {
            bool geyserOK = true;
            if (power >= highManitudeTreshold)
            {
                selectedCell = GetHighMagnitudeEpicenter(power);
                geyserOK = selectedCell > -1;
            }
            if (power < highManitudeTreshold || selectedCell == -1)
            {
                float min = power;
                selectedChunk = FindChunk(min);
                selectedCell = FindCell(selectedChunk, min);
            }

            cell = selectedCell;
            return geyserOK && cell > -1;
        }

        public static bool IsActiveChunk(int cell)
        {
            Grid.CellToXY(cell, out int x, out int y);
            return selectedChunk == XYToChunk(x / CHUNK_EDGE, y / CHUNK_EDGE);
        }

        private static void SetChunks()
        {
            widthInChunks = Grid.WidthInCells / CHUNK_EDGE;
            heightInChunks = Grid.HeightInCells / CHUNK_EDGE;
            chunks = new Dictionary<int, float>();

            for (int x = 0; x < widthInChunks; x++)
            {
                for (int y = 0; y < heightInChunks; y++)
                {
                    float max = FindMax(x, y);
                    chunks[XYToChunk(x, y)] = max;
                    if (max > highestActivity) highestActivity = max;
                }
            }
        }

        private static void CreateSafeZones()
        {
            foreach (Telepad printingPod in Components.Telepads)
            {
                MarkSafeZone(Grid.PosToCell(printingPod), ModAssets.settings.EarthquakeSafeRadius);
            }
        }

        public static void MarkSafeZone(int center, int r)
        {
            Grid.CellToXY(center, out int bx, out int by);
            for (int x = bx - r; x < bx + r * 2; x++)
            {
                for (int y = by - r; y < by + r * 2; y++)
                {
                    int cell = Grid.XYToCell(x, y);
                    int dist = Grid.GetCellDistance(cell, center);
                    if (dist <= r)
                    {
                        float hr = r / 2;
                        if (dist <= hr)
                            activity[cell] = 0;
                        else
                            activity[cell] *= (dist - hr) / hr;
                    }
                }
            }
        }

        private static int FindChunk(float treshold)
        {
            var eligibleChunks = chunks.Where(c => c.Value >= treshold).ToList();
            if (eligibleChunks.Count > 0)
                return eligibleChunks.GetRandom().Key;
            else return -1;
        }

        private static int FindCell(int chunk, float min)
        {
            ChunkOffset(chunk, out int x, out int y);
            int attempt = 0;
            int maxAttempts = CHUNK_EDGE * CHUNK_EDGE;

            // start at random cell
            Vector2I cellPos = new Vector2I(
                Random.Range(0, CHUNK_EDGE - 1) + x,
                Random.Range(0, CHUNK_EDGE - 1) + y);

            // iterate through the other cells in order
            while (attempt++ < maxAttempts)
            {
                int cell = Grid.XYToCell(cellPos.x, cellPos.y);
                if (activity[cell] >= min)
                {
                    return cell;
                }

                cellPos = NextCell(cellPos, x, y);
            }

            return -1;
        }

        private static float FindMax(int cx, int cy)
        {
            float max = 0;

            for (int x = 0; x < CHUNK_EDGE; x++)
            {
                for (int y = 0; y < CHUNK_EDGE; y++)
                {
                    float num = activity[Grid.XYToCell(cx * CHUNK_EDGE + x, cy * CHUNK_EDGE + y)];
                    if (num > max) max = num;
                }
            }

            return max;
        }
        public static float GetChunkMaxActivity(int cell)
        {
            Grid.CellToXY(cell, out int x, out int y);
            int index = XYToChunk(x / CHUNK_EDGE, y / CHUNK_EDGE);
            return chunks[index];
        }

        private static Vector2I NextCell(Vector2I pos, int chunkX, int chunkY)
        {
            if (pos.x < chunkX + CHUNK_EDGE - 1)
            {
                // move one to right
                return new Vector2I(pos.x + 1, pos.y);
            }

            if (pos.y < chunkY + CHUNK_EDGE - 1)
            {
                // move to next line
                return new Vector2I(chunkX, pos.y + 1);
            }

            // move to first cell
            return new Vector2I(chunkX, chunkY);
        }

        public static int XYToChunk(int x, int y)
        {
            return x + y * widthInChunks;
        }

        public static void ChunkToXY(int chunk, out int x, out int y)
        {
            x = chunk % widthInChunks;
            y = chunk / widthInChunks;
        }

        public static void ChunkOffset(int chunk, out int x, out int y)
        {
            x = (chunk % widthInChunks) * CHUNK_EDGE;
            y = (chunk / widthInChunks) * CHUNK_EDGE;
        }

    }
}