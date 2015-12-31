using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using SharpDX;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Windows;
using Buffer = SharpDX.Direct3D11.Buffer;
using Device = SharpDX.Direct3D11.Device;
using MapFlags = SharpDX.Direct3D11.MapFlags;
using FLib.SharpDX;
using DXColor = SharpDX.Color;
namespace PatchworkLib.PatchMesh
{
    public class PatchMeshRenderResources : IDisposable
    {
        Dictionary<string, Texture2D> textureDict = new Dictionary<string, Texture2D>();

        /// <summary>
        /// メッシュとパッチを組み合わせたキーを作成する
        /// obj, patchKeyが同一なら常に同じキーが生成される
        /// </summary>
        /// <param name="obj"></param>
        /// <param name="patchKey"></param>
        /// <returns></returns>
        public static string GenerateResourceKey(PatchMesh obj, string patchKey)
        {
            if (obj == null)
                return ":" + patchKey;
            string key = obj.Id + ":" + patchKey;
            System.Diagnostics.Debug.Assert(key.Count(c => c == ':') == 1);
            return key;
        }

        public void Add(string resourceKey, Texture2D texture)
        {
            if (textureDict.ContainsKey(resourceKey))
            {
                if (textureDict[resourceKey] != null && !textureDict[resourceKey].IsDisposed)
                    textureDict[resourceKey].Dispose();
            }
            textureDict[resourceKey] = texture;
        }


        public void Remove(string resourceKey)
        {
            if (textureDict.ContainsKey(resourceKey))
            {
                if (textureDict[resourceKey] != null && !textureDict[resourceKey].IsDisposed)
                    textureDict[resourceKey].Dispose();
                textureDict.Remove(resourceKey);
            }
        }
        public Texture2D GetTexture(string resourceKey)
        {
            if (textureDict.ContainsKey(resourceKey))
                if (textureDict[resourceKey] != null && !textureDict[resourceKey].IsDisposed)
                    return textureDict[resourceKey];
            return null;
        }

        public List<string> GetResourceKeyByPatchMesh(PatchMesh m)
        {
            List<string> ls = new List<string>();
            string prefix = m.Id + ":";
            foreach (var key in textureDict.Keys)
                if (key.StartsWith(prefix))
                    ls.Add(key);
            return ls;
        }

        public void Dispose()
        {
            if (textureDict != null)
                foreach (var kv in textureDict)
                    kv.Value.Dispose();
        }

        public void DuplicateResources(PatchMesh from, PatchMesh to)
        {
            string prefix_from = from.Id + ":";
            string prefix_to = to.Id + ":";
            var keys = textureDict.Keys.Where(k => k.StartsWith(prefix_from)).ToList();
            foreach (var k in keys)
            {
                var newKey = prefix_to + k.Substring(prefix_from.Length);
                textureDict[newKey] = textureDict[k];
            }
        }

        public void RemoveResources(PatchMesh m)
        {
            string prefix = m.Id + ":";
            var keys = textureDict.Keys.Where(k => k.StartsWith(prefix)).ToList();
            foreach (var k in keys)
                textureDict.Remove(k);
        }

        public Dictionary<string, Texture2D> CopyTextureDict()
        {
            var dict = textureDict.ToDictionary(kv => kv.Key, kv => kv.Value);
            return dict;
        }

    }
}
