﻿using System;
using System.Collections.Generic;
using System.Text;

namespace ManicDigger
{
    public interface IShadows
    {
        void OnLocalBuild(int x, int y, int z);
        void OnSetBlock(int x, int y, int z);
        void ResetShadows();
        int GetLight(int x, int y, int z);
        int maxlight { get; }
        void OnGetTerrainBlock(int x, int y, int z);
    }
    public class ShadowsSimple : IShadows
    {
        [Inject]
        public IGameData data { get; set; }
        [Inject]
        public IMapStorage map { get; set; }
        int defaultshadow = 11;
        #region IShadows Members
        public void OnLocalBuild(int x, int y, int z)
        {
        }
        public void OnSetBlock(int x, int y, int z)
        {
        }
        public void ResetShadows()
        {
        }
        public int GetLight(int x, int y, int z)
        {
            return IsShadow(x, y, z) ? defaultshadow : maxlight;
        }
        public int maxlight
        {
            get { return 16; }
        }
        public void OnGetTerrainBlock(int x, int y, int z)
        {
        }
        #endregion
        private bool IsShadow(int x, int y, int z)
        {
            for (int i = 1; i < 10; i++)
            {
                if (MapUtil.IsValidPos(map, x, y, z + i) && !data.GrassGrowsUnder(map.GetBlock(x, y, z + i)))
                {
                    return true;
                }
            }
            return false;
        }
    }
    public class Shadows : IShadows
    {
        public IMapStorage map { get; set; }
        public IGameData data { get; set; }
        public ITerrainDrawer terrain { get; set; }

        const int chunksize = 16;
        Queue<Vector3i> shadowstoupdate = new Queue<Vector3i>();
        public void OnLocalBuild(int x, int y, int z)
        {
            lock (lighttoupdate_lock)
            {
                UpdateShadows(x, y, z);
            }
        }
        public void OnSetBlock(int x, int y, int z)
        {
            shadowstoupdate.Enqueue(new Vector3i(x, y, z));
        }
        private void UpdateShadows(int x, int y, int z)
        {
            lighttoupdate.Clear();
            UpdateSunlight(x, y, z);
            List<Vector3i> near = new List<Vector3i>();
            near.Add(new Vector3i(x, y, z));
            foreach (var n in BlocksNear(x, y, z))
            {
                if (MapUtil.IsValidPos(map, n.x, n.y, n.z))
                {
                    near.Add(n);
                }
            }
            if (near.Count > 0)
            {
                DefloodLight(near);
            }
            foreach (var k in lighttoupdate)
            {
                terrain.UpdateTile(k.Key.x, k.Key.y, k.Key.z);
            }
        }
        Dictionary<Vector3i, Vector3i> lighttoupdate = new Dictionary<Vector3i, Vector3i>();
        object lighttoupdate_lock = new object();
        public bool loaded = false;
        public void ResetShadows()
        {
            light = null;
            lightheight = null;
            chunklighted = null;
            UpdateHeightCache();
            loaded = true;
        }
        private void UpdateLight()
        {
            light = new byte[map.MapSizeX, map.MapSizeY, map.MapSizeZ];
            UpdateHeightCache();
            for (int x = 0; x < map.MapSizeX; x++)
            {
                for (int y = 0; y < map.MapSizeY; y++)
                {
                    for (int z = 0; z < map.MapSizeZ; z++)
                    {
                        if (z >= lightheight[x, y])
                        {
                            light[x, y, z] = (byte)maxlight;
                        }
                        else
                        {
                            light[x, y, z] = (byte)minlight;
                        }
                    }
                }
            }
        }
        private void UpdateHeightCache()
        {
            if (lightheight == null)
            {
                lightheight = new int[map.MapSizeX, map.MapSizeY];
            }
            for (int x = 0; x < map.MapSizeX; x++)
            {
                for (int y = 0; y < map.MapSizeY; y++)
                {
                    UpdateLightHeightmapAt(x, y);
                }
            }
        }
        private void UpdateLightHeightmapAt(int x, int y)
        {
            lightheight[x, y] = map.MapSizeZ - 1;
            for (int z = map.MapSizeZ - 1; z >= 0; z--)
            {
                if (data.GrassGrowsUnder(map.GetBlock(x, y, z)))
                {
                    lightheight[x, y]--;
                }
                else
                {
                    break;
                }
            }
        }
        int[,] lightheight;
        byte[, ,] light;
        int minlight = 1;
        public int maxlight { get { return 16; } }
        void UpdateSunlight(int x, int y, int z)
        {
            if (lightheight == null) { ResetShadows(); }
            int oldheight = lightheight[x, y];
            if (oldheight < 0) { oldheight = 0; }
            if (oldheight >= map.MapSizeZ) { oldheight = map.MapSizeZ - 1; }
            UpdateLightHeightmapAt(x, y);
            int newheight = lightheight[x, y];
            if (newheight < oldheight)
            {
                //make white
                for (int i = newheight; i <= oldheight; i++)
                {
                    SetLight(x, y, i, maxlight);
                    FloodLight(x, y, i);
                }
            }
            if (newheight > oldheight)
            {
                //make black
                for (int i = oldheight; i <= newheight; i++)
                {
                    SetLight(x, y, i, minlight);
                    //DefloodLight(x, i);

                    List<Vector3i> deflood = new List<Vector3i>();
                    foreach (var n in BlocksNear(x, y, i))
                    {
                        if (MapUtil.IsValidPos(map, n.x, n.y, n.z))
                        {
                            deflood.Add(n);
                        }
                    }
                    if (deflood.Count != 0)
                    {
                        DefloodLight(deflood);
                    }
                }
            }
        }
        void SetLight(int x, int y, int z, int value)
        {
            light[x, y, z] = (byte)value;
            lighttoupdate[new Vector3i((x / 16) * 16 + 5, (y / 16) * 16 + 5, (z / 16) * 16 + 5)] = new Vector3i();
            foreach (Vector3i v in BlocksNear(x, y, z))
            {
                if (v.x / 16 != x / 16
                    || v.y / 16 != y / 16
                    || v.z / 16 != z / 16)
                {
                    lighttoupdate[new Vector3i((v.x / 16) * 16 + 5, (v.y / 16) * 16 + 5, (v.z / 16) * 16 + 5)] = new Vector3i();
                }
            }
        }
        private void DefloodLight(IEnumerable<Vector3i> start)
        {
            Queue<Vector3i> q = new Queue<Vector3i>();
            Vector3i ss = new Vector3i();
            foreach (var s in start)
            {
                q.Enqueue(s);
                ss = s;
            }
            Dictionary<Vector3i, bool> reflood = new Dictionary<Vector3i, bool>();
            for (; ; )
            {
                if (q.Count == 0)
                {
                    break;
                }
                Vector3i v = q.Dequeue();
                if (distancesquare(v, new Vector3i(ss.x, ss.y, ss.z)) > maxlight * 2 * maxlight * 2)
                {
                    continue;
                }
                if (!data.GrassGrowsUnder(map.GetBlock(v.x, v.y, v.z))
                    && !data.IsLightEmitting(map.GetBlock(v.x, v.y, v.z)))
                {
                    continue;
                }
                if (light[v.x, v.y, v.z] == maxlight
                    || data.IsLightEmitting(map.GetBlock(v.x, v.y, v.z)))
                {
                    reflood[v] = true;
                    continue;
                }
                if (light[v.x, v.y, v.z] == minlight)
                {
                    continue;
                }
                SetLight(v.x, v.y, v.z, minlight);
                foreach (var n in BlocksNear(v.x, v.y, v.z))
                {
                    if (!MapUtil.IsValidPos(map, n.x, n.y, n.z))
                    {
                        continue;
                    }
                    if (light[n.x, n.y, n.z] > light[v.x, v.y, v.z])
                    {
                        q.Enqueue(n);
                    }
                }
            }
            foreach (var p in reflood.Keys)
            {
                FloodLight(p.x, p.y, p.z);
            }
        }
        private int distancesquare(Vector3i a, Vector3i b)
        {
            int dx = a.x - b.x;
            int dy = a.y - b.y;
            int dz = a.z - b.z;
            return dx * dx + dy * dy + dz * dz;
        }
        private void FloodLight(int x, int y, int z)
        {
            if (light == null)
            {
                UpdateLight();
            }
            if (data.IsLightEmitting(map.GetBlock(x, y, z)))
            {
                light[x, y, z] = (byte)(maxlight - 1);
            }
            Queue<Vector3i> q = new Queue<Vector3i>();
            q.Enqueue(new Vector3i(x, y, z));
            for (; ; )
            {
                if (q.Count == 0)
                {
                    break;
                }
                Vector3i v = q.Dequeue();
                if (distancesquare(v, new Vector3i(x, y, z)) > maxlight * maxlight)
                {
                    continue;
                }
                if (light[v.x, v.y, v.z] == minlight)
                {
                    continue;
                }
                if (!data.GrassGrowsUnder(map.GetBlock(v.x, v.y, v.z))
                    && !data.IsLightEmitting(map.GetBlock(v.x, v.y, v.z)))
                {
                    continue;
                }
                foreach (var n in BlocksNear(v.x, v.y, v.z))
                {
                    if (!MapUtil.IsValidPos(map, n.x, n.y, n.z))
                    {
                        continue;
                    }
                    if (light[n.x, n.y, n.z] < light[v.x, v.y, v.z] - 1)
                    {
                        SetLight(n.x, n.y, n.z, (byte)(light[v.x, v.y, v.z] - 1));
                        q.Enqueue(n);
                    }
                }
            }
        }
        private IEnumerable<Vector3i> BlocksNear(int x, int y, int z)
        {
            yield return new Vector3i(x - 1, y, z);
            yield return new Vector3i(x + 1, y, z);
            yield return new Vector3i(x, y - 1, z);
            yield return new Vector3i(x, y + 1, z);
            yield return new Vector3i(x, y, z - 1);
            yield return new Vector3i(x, y, z + 1);
        }
        private IEnumerable<Vector3i> BlocksNearWith(int x, int y, int z)
        {
            yield return new Vector3i(x, y, z);
            yield return new Vector3i(x - 1, y, z);
            yield return new Vector3i(x + 1, y, z);
            yield return new Vector3i(x, y - 1, z);
            yield return new Vector3i(x, y + 1, z);
            yield return new Vector3i(x, y, z - 1);
            yield return new Vector3i(x, y, z + 1);
        }
        private void FloodLightChunk(int x, int y, int z)
        {
            for (int xx = 0; xx < chunksize; xx++)
            {
                for (int yy = 0; yy < chunksize; yy++)
                {
                    for (int zz = 0; zz < chunksize; zz++)
                    {
                        FloodLight(x + xx, y + yy, z + zz);
                    }
                }
            }
        }
        bool IsValidChunkPos(int cx, int cy, int cz)
        {
            return cx >= 0 && cy >= 0 && cz >= 0
                && cx < map.MapSizeX / chunksize
                && cy < map.MapSizeY / chunksize
                && cz < map.MapSizeZ / chunksize;
        }
        bool[, ,] chunklighted;
        public int GetLight(int x, int y, int z)
        {
            if (loaded)
            {
                while (shadowstoupdate.Count > 0)
                {
                    Vector3i p = shadowstoupdate.Dequeue();
                    UpdateShadows(p.x, p.y, p.z);
                }
            }
            if (light == null)
            {
                UpdateLight();
            }
            return light[x, y, z];
        }
        public void OnGetTerrainBlock(int x, int y, int z)
        {
            if (chunklighted == null)
            {
                chunklighted = new bool[map.MapSizeX / chunksize, map.MapSizeY / chunksize, map.MapSizeZ / chunksize];
            }
            foreach (var k in BlocksNear(x / chunksize, y / chunksize, z / chunksize))
            {
                if (!IsValidChunkPos(k.x, k.y, k.z))
                {
                    continue;
                }
                if (!chunklighted[k.x, k.y, k.z])
                {
                    lock (lighttoupdate_lock)
                    {
                        FloodLightChunk(k.x * chunksize, k.y * chunksize, k.z * chunksize);
                        chunklighted[k.x, k.y, k.z] = true;
                    }
                }
            }
        }
    }
}