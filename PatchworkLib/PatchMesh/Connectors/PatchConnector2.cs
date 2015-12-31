#define _DEBUG
// expand()
// texture synthesis()
// resample();


using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using FLib;
using PatchSection = System.Drawing.CharacterRange;

namespace PatchworkLib.PatchMesh
{
    /// <summary>
    /// BiggerPicture を参考にした実装
    /// PatchConnectorに比べて格段に綺麗につながって見えるはず
    /// 接続後にテクスチャ合成・メッシュのリサンプルも行う
    /// </summary>

    public class PatchConnector2
    {
        /// <summary>
        /// 
        /// - ざっくり位置合わせ（オーバーラップさせる）
        /// - 接続後の境界を計算
        /// - それにあせてボーンと垂直方向に画像を変形
        /// - リサンプリング
        /// 
        /// ・対応する境界線を探す(2D)
        /// ・ざっくり重なるように移動・拡大操作(2D)
        /// ・seam carving method(3D?)
        /// ・graphcut textures(2D)
        /// ・poisson blending(2D) (opencvを使う)
        /// ・resampleしてpatchmeshに戻す(3D)
        /// </summary>
        /// <param name="patches"></param>
        /// <param name="refSkeleton"></param>
        /// <param name="resources"></param>
        /// <returns></returns>
        public static PatchSkeletalMesh Connect(List<PatchSkeletalMesh> patches, PatchSkeleton refSkeleton, PatchMeshRenderResources resources)
        {
            var overlapPairs = OverlapPatchPairs(patches, refSkeleton);
            if (overlapPairs.Count <= 0)
                return null;

            var newPatch = overlapPairs[0].Item1;
            List<PatchSkeletalMesh> aggregated = new List<PatchSkeletalMesh>();
            aggregated.Add(overlapPairs[0].Item1);

            for (int j = 0; j < overlapPairs.Count; j++)
            {
                // newPatchと接続可能なpatchをひとつ探して繋げる
                // O(N^2)だけど、patchesの総数はせいぜい十数個なので問題ない
                for (int i = 0; i < overlapPairs.Count; i++)
                {
                    var pair = overlapPairs[i];
                    if (aggregated.Contains(pair.Item1) && !aggregated.Contains(pair.Item2))
                    {
                        var connected = Connect(newPatch, pair.Item2, refSkeleton, resources);
                        if (connected != null)
                        {
                            newPatch = connected;
                            aggregated.Add(pair.Item2);
                            overlapPairs.RemoveAt(i);
                            break;
                        }
                    }
                    else if (!aggregated.Contains(pair.Item1) && aggregated.Contains(pair.Item2))
                    {
                        if (newPatch != null)
                        {
                            var connected = Connect(newPatch, pair.Item1, refSkeleton, resources);
                            if (connected != null)
                            {
                                newPatch = connected;
                                aggregated.Add(pair.Item1);
                                overlapPairs.RemoveAt(i);
                                break;
                            }
                        }
                    }
                }
            }

            return newPatch;
        }

        public static PatchSkeletalMesh Connect(PatchSkeletalMesh patch1, PatchSkeletalMesh patch2, PatchSkeleton refSkeleton, PatchMeshRenderResources resources)
        {
            if (refSkeleton == null)
                return null;

            if (!System.IO.Directory.Exists("output_Connector2"))
                System.IO.Directory.CreateDirectory("output_Connector2");

            FLib.FileManager.OpenExplorer("output_Connector2");

#if _DEBUG
            PatchSkeletalMeshRenderer.ToBitmap(patch1).Save("output_Connector2/1_patch1.png");
            PatchSkeletalMeshRenderer.ToBitmap(patch2).Save("output_Connector2/1_patch2.png");
#endif
            // 1. パッチをコピー
            PatchSkeletalMesh patch1_t = PatchSkeletalMesh.Copy(patch1);
            PatchSkeletalMesh patch2_t = PatchSkeletalMesh.Copy(patch2);
            var refSkeleton_t = PatchSkeleton.Copy(refSkeleton);

            // 2. patch1, patch2の接続面および対応するボーンを探す
            PatchSection section1;
            PatchSection section2;
            PatchSkeletonBone crossingBone;
            bool swap;
            bool found = ConnectableSections(patch1_t, patch2_t, refSkeleton_t, out section1, out section2, out crossingBone, out swap);
            if (!found)
                return null;

            if (swap)
            {
                FMath.Swap(ref patch1_t, ref patch2_t);
                FMath.Swap(ref section1, ref section2);
            }

#if _DEBUG
            PatchSkeletalMeshRenderer.ToBitmap(patch1_t, new List<CharacterRange>() { section1 }).Save("output_Connector2/2_patch1.png");
            PatchSkeletalMeshRenderer.ToBitmap(patch2_t, new List<CharacterRange>() { section2 }).Save("output_Connector2/2_patch2.png");
#endif

            // 3. 2つのパッチが重なるように位置調整およびARAP変形
            patch1_t.mesh.BeginDeformation();
            patch2_t.mesh.BeginDeformation();
            Deform(patch1_t, patch2_t, refSkeleton_t, section1, section2, crossingBone);
            patch2_t.mesh.EndDeformation();
            patch1_t.mesh.EndDeformation();

#if _DEBUG
            PatchSkeletalMeshRenderer.ToBitmap(patch1_t, new List<CharacterRange>() { section1 }).Save("output_Connector2/3_patch1.png");
            PatchSkeletalMeshRenderer.ToBitmap(patch2_t, new List<CharacterRange>() { section2 }).Save("output_Connector2/3_patch2.png");
#endif

            // 4. 2つのパッチのテクスチャを合成して、新しいパッチを生成
            
//            TODO：メッシュをビットマップ画像として書き出してリサンプリング

            // todo
            var combinedSMesh = Combine(patch1_t, patch2_t, section1, section2);


            // 5. 新しいパッチに使うテクスチャをリソースに登録

            // todo
            if (resources != null)
            {
                List<string> textureKeys = resources.GetResourceKeyByPatchMesh(patch1.mesh);
                textureKeys.AddRange(resources.GetResourceKeyByPatchMesh(patch2.mesh));

                foreach (var key in textureKeys)
                {
                    string patchKey = key.Split(':').Last();
                    string newKey = PatchMeshRenderResources.GenerateResourceKey(combinedSMesh.mesh, patchKey);
                    resources.Add(newKey, resources.GetTexture(key));
                }
            }
            return combinedSMesh;
        }

        //----------------------------------------------------------------

#region Connectability

        static List<Tuple<PatchSkeletalMesh, PatchSkeletalMesh>> OverlapPatchPairs(List<PatchSkeletalMesh> smeshes, PatchSkeleton refSkeleton)
        {
            var overlaps = new Dictionary<PatchSkeletonBone, List<PatchSkeletalMesh>>();
            foreach (var b in refSkeleton.bones)
                overlaps[b] = new List<PatchSkeletalMesh>();

            foreach (var sm in smeshes)
            {
                foreach (var b in sm.skl.bones)
                {
                    if (overlaps.ContainsKey(b))
                    {
                        overlaps[b].Add(sm);
                        if (overlaps[b].Count >= 3)
                            return new List<Tuple<PatchSkeletalMesh, PatchSkeletalMesh>>();
                    }
                }
            }

            var pairs = new List<Tuple<PatchSkeletalMesh, PatchSkeletalMesh>>();

            foreach (var kv in overlaps)
            {
                if (kv.Value.Count != 2)
                    continue;
                pairs.Add(new Tuple<PatchSkeletalMesh, PatchSkeletalMesh>(kv.Value[0], kv.Value[1]));
            }

            return pairs;
        }

        public static bool IsConnectable(List<PatchSkeletalMesh> smeshes, PatchSkeleton refSkeleton)
        {
            if (smeshes == null || refSkeleton == null || smeshes.Count <= 1)
                return false;

            // 1) ボーンの重なりが最大2
            // 2) 重なっているボーンで全てのメッシュが連結される
            // -FindConnectingSections() == true

            var overlapPairs = OverlapPatchPairs(smeshes, refSkeleton);

            HashSet<PatchSkeletalMesh> overlapSMeshes = new HashSet<PatchSkeletalMesh>();

            foreach (var pair in overlapPairs)
            {
                overlapSMeshes.Add(pair.Item1);
                overlapSMeshes.Add(pair.Item2);
            }

            if (overlapSMeshes.Count != smeshes.Count)
                return false;

            foreach (var p in overlapPairs)
            {
                PatchSection section1, section2;
                PatchSkeletonBone crossingBone;
                bool swap;
                bool found = ConnectableSections(p.Item1, p.Item2, refSkeleton, out section1, out section2, out crossingBone, out swap);

                if (!found)
                {
                    return false;
                }
            }

            return true;
        }

        
        //
        // ConnectableSections()
        //
        
        /// skl内の同一のボーンと交差していて、なおかつ向きが逆の切り口を探す
        private static bool ConnectableSections(
            PatchSkeletalMesh smesh1, PatchSkeletalMesh smesh2, PatchSkeleton skl,
            out PatchSection section1, out PatchSection section2, out PatchSkeletonBone crossingBone, out bool swap)
        {
            section1 = new PatchSection(0, -1);
            section2 = new PatchSection(0, -1);
            crossingBone = null;
            swap = false;

            if (skl == null)
                return false;

            if (smesh1 == null || smesh2 == null)
                return false;

            foreach (var bone in skl.bones)
            {
                List<PatchSection> sections1, sections2;
                List<float> dir1, dir2;

                // 各切り口がboneと交差しているか
                if (!FindSectionsByBone(smesh1, bone, out sections1, out dir1))
                    continue;
                if (!FindSectionsByBone(smesh2, bone, out sections2, out dir2))
                    continue;

                for (int i = 0; i < sections1.Count; i++)
                {
                    for (int j = 0; j < sections2.Count; j++)
                    {
                        // ２つの切り口が逆向きか
                        if (dir1[i] * dir2[j] >= 0)
                            continue;
                        // bone.src側がsection1になるようにする
                        if (dir1[i] < 0)
                            swap = false;
                        else
                            swap = true;
                        section1 = sections1[i];
                        section2 = sections2[j];
                        crossingBone = bone;
                        return true;
                    }
                }
            }

            return false;
        }

        static bool FindSectionsByBone(PatchSkeletalMesh smesh, PatchSkeletonBone refBone, out List<PatchSection> sections, out List<float> dirs)
        {
            sections = new List<PatchSection>();
            dirs = new List<float>();

            // _boneに対応するmesh.skl内のボーンを探す
            PatchSkeletonBone bone = null;
            foreach (var b in smesh.skl.bones)
                if (b == refBone)
                    bone = b;

            if (bone == null)
                return false;

            foreach (var sec in smesh.sections)
            {
                for (int i = sec.First; i < sec.First + sec.Length - 1; i++)
                {
                    int n = smesh.mesh.pathIndices.Count;
                    var p1 = smesh.mesh.vertices[smesh.mesh.pathIndices[FMath.Rem(i, n)]].position;
                    var p2 = smesh.mesh.vertices[smesh.mesh.pathIndices[FMath.Rem(i + 1, n)]].position;
                    if (FLib.FMath.IsCrossed(p1, p2, bone.src.position, bone.dst.position))
                    {
                        sections.Add(sec);

                        // 交差点からボーン方向に少し動かした点がメッシュ内か否かで切り口の向きを判定
                        PointF crossPoint = FLib.FMath.CrossPoint(p1, p2, bone.src.position, bone.dst.position);
                        PointF boneDir = new PointF(bone.src.position.X - bone.dst.position.X, bone.src.position.Y - bone.dst.position.Y);
                        float ratio = 1f / FLib.FMath.Distance(bone.src.position, bone.dst.position);
                        float sampleX = crossPoint.X + boneDir.X * ratio;
                        float sampleY = crossPoint.Y + boneDir.Y * ratio;
                        bool inMesh = FLib.FMath.IsPointInPolygon(new PointF(sampleX, sampleY), GetPath(smesh).Select(v => v.position).ToList());
                        dirs.Add(inMesh ? -1 : 1);
                    }
                }
            }

            if (sections.Count >= 1)
                return true;

            return false;
        }
#endregion

#region Deform

        //-----------------------------------------------------------------------------------
        // Deform()
        //-----------------------------------------------------------------------------------

        /// <summary>
        /// 切り口付近で２つのsmeshが重なるように変形する
        /// patch2がdst方向
        /// </summary>
        private static void Deform(PatchSkeletalMesh patch1, PatchSkeletalMesh patch2, PatchSkeleton refSkeleton, PatchSection section1, PatchSection section2, PatchSkeletonBone crossingBone)
        {
            if (patch1 == null || patch2 == null)
                return;

            // 各メッシュを大雑把にスケルトンに合わせる
            PatchSkeletonFitting.Fitting(patch1, refSkeleton);
            PatchSkeletonFitting.Fitting(patch2, refSkeleton);

            // TODO: サイズの修正

            // 回転はFitting()でやってるから必要ない            

            // 位置の調整

            float overlap = OverlapWidth(patch1, patch2, refSkeleton, section1, section2, crossingBone, 100, 45);

            AdjustHeight(patch1, patch2, refSkeleton, section1, section2, crossingBone);
            OverlayPatches(patch1, patch2, refSkeleton, section1, section2, crossingBone, overlap);

#if _DEBUG
            PatchSkeletalMeshRenderer.ToBitmap(patch1).Save("output_Connector2/4_patch1.png");
            PatchSkeletalMeshRenderer.ToBitmap(patch2).Save("output_Connector2/4_patch2.png");
#endif
            // メッシュを伸ばして繋げる
            Expand(patch1, patch2, refSkeleton, section1, section2, crossingBone, overlap, overlap);
        }

        static PatchSkeletonBone RefBone(PatchSkeleton refSkeleton, PatchSkeletonBone bone)
        {
            PatchSkeletonBone refBone = null;
            foreach (var b in refSkeleton.bones)
            {
                if (bone == b)
                {
                    refBone = b;
                    break;
                }
            }
            return refBone;
        }


        // 
        // AdjustPosition()
        //

        static void AdjustHeight(PatchSkeletalMesh smesh1, PatchSkeletalMesh smesh2, PatchSkeleton refSkeleton,
            PatchSection section1, PatchSection section2, PatchSkeletonBone bone)
        {
            PatchSkeletonBone refBone = RefBone(refSkeleton, bone);
            if (refBone == null)
                return;

            // 切り口の中心とボーンの軸とのずれ（ボーンと垂直な方向について）
            float height1 = SectionHeight(smesh1, section1, refBone, 30);
            float height2 = SectionHeight(smesh2, section2, refBone, 30);

            // ボーンと水平なベクトルと垂直なベクトルをそれぞれx, yとして取得
            PointF x, y;
            BoneCoordinate(refBone, out x, out y);

            // smesh2の切り口の中心がsmesh1の切り口の中心に重なるようにsmesh2をずらす
            if (Math.Abs(height1 - height2) > 1e-4)
            {
                float dx = y.X * (height1 - height2);
                float dy = y.Y * (height1 - height2);

                // mesh2の頂点・制御点を平行移動する
                foreach (var v in smesh2.mesh.vertices)
                    v.position = new PointF(v.position.X + dx, v.position.Y + dy);
                foreach (var c in smesh2.mesh.CopyControlPoints())
                    smesh2.mesh.TranslateControlPoint(c.position, new PointF(c.position.X + dx, c.position.Y + dy), false);
            }
        }

        /// <param name="checkLength">sectionから何pixel分の輪郭を計算に使うか/param>
        static float SectionHeight(PatchSkeletalMesh mesh, PatchSection section, PatchSkeletonBone b, float curveLength)
        {
            List<PatchVertex> path = GetPath(mesh);
            var rawcurves = section2adjCurves(path, section);
            if (path == null || rawcurves == null || b == null)
                return 0;

            var curves = TrimCurves(path, rawcurves, 0, curveLength);
            if (curves.Item1 == null || curves.Item2 == null)
                return 0;

            float boneLen = FMath.Distance(b.src.position, b.dst.position);
            if (boneLen <= 1e-4)
                return 0;

            PointF x, y;
            BoneCoordinate(b, out x, out y);

            // 各セグメントのボーンからのズレを求める
            float height = 0;
            int cnt = Math.Min(curves.Item1.Length, curves.Item2.Length);
            for (int i = 0; i < cnt; i++)
            {
                int idx1 = curves.Item1.First + curves.Item1.Length - 1 - i;
                int idx2 = curves.Item2.First + i;
                while (idx1 < 0)
                    idx1 += path.Count;
                while (idx2 < 0)
                    idx2 += path.Count;
                PointF pt1 = path[idx1 % path.Count].position;
                PointF pt2 = path[idx2 % path.Count].position;
                pt1.X -= b.src.position.X;
                pt1.Y -= b.src.position.Y;
                pt2.X -= b.src.position.X;
                pt2.Y -= b.src.position.Y;
                height += pt1.X * y.X + pt1.Y * y.Y;
                height += pt2.X * y.X + pt2.Y * y.Y;
            }

            height /= 2 * cnt;

            return height;
        }

        // smeshの輪郭線を取得
        static List<PatchVertex> GetPath(PatchSkeletalMesh mesh)
        {
            List<PatchVertex> path = new List<PatchVertex>();
            for (int i = 0; i < mesh.mesh.pathIndices.Count; i++)
                path.Add(mesh.mesh.vertices[mesh.mesh.pathIndices[i]]);
            return path;
        }

        static void BoneCoordinate(PatchSkeletonBone b, out PointF x, out PointF y)
        {
            x = new PointF(1, 0);
            y = new PointF(0, 1);

            float boneLen = FMath.Distance(b.src.position, b.dst.position);
            if (boneLen <= 1e-4)
                return;

            float dx = (b.dst.position.X - b.src.position.X) / boneLen;
            float dy = (b.dst.position.Y - b.src.position.Y) / boneLen;

            x = new PointF(dx, dy);
            y = new PointF(dy, -dx);
        }

        private static float OverlapWidth(PatchSkeletalMesh patch1, PatchSkeletalMesh patch2, PatchSkeleton refSkeleton, PatchSection section1, PatchSection section2, PatchSkeletonBone bone, int maxLength, int maxAngle)
        {
            var refBone = RefBone(refSkeleton, bone);

            var path1 = GetPath(patch1);
            var curves1 = section2adjCurves(path1, section1);
            var sorted1 = GetSortedCurves(path1.Select(v => v.position).ToList(), curves1, refBone);

            var path2 = GetPath(patch2);
            var curves2 = section2adjCurves(path2, section2);
            var sorted2 = GetSortedCurves(path2.Select(v => v.position).ToList(), curves2, refBone);

            float[] param = new float[] { 0, 0, 0, 0 };
            var sortedCurves = new[] {
                sorted1[0],
                sorted1[1],
                sorted2[0],
                sorted2[1],
            };

            float minCos = (float)Math.Cos(Math.PI / 180 * maxAngle);
            for (int i = 0; i < sortedCurves.Length; i++)
            {
                var c = sortedCurves[i];
                var path = i < 2 ? path1 : path2;
                List<PointF> pts = new List<PointF>();
                float total = 0;
                for (int j = c.Count - 1; j >= 0; j--)
                {
                    var p = c[j];
                    pts.Add(p);
                    if (pts.Count >= 3 && FMath.GetAngleCos(pts) < minCos)
                    {
                        pts.RemoveAt(pts.Count - 1);
                        break;
                    }
                    if (pts.Count >= 2)
                        total += FMath.Distance(pts[pts.Count - 2], pts[pts.Count - 1]);
                    if (total >= maxLength)
                    {
                        pts.RemoveAt(pts.Count - 1);
                        break;
                    }
                }
                param[i] = FMath.ParameterOnLine(pts.Last(), refBone.src.position, refBone.dst.position);
            }

            float min1 = Enumerable.Range(section1.First, section1.Length).Min(idx =>
                FMath.ParameterOnLine(path1[FMath.Rem(idx, path1.Count)].position, refBone.src.position, refBone.dst.position));
            float max1 = Enumerable.Range(section1.First, section1.Length).Max(idx =>
                FMath.ParameterOnLine(path1[FMath.Rem(idx, path1.Count)].position, refBone.src.position, refBone.dst.position));

            int idx1 = Enumerable.Range(section1.First, section1.Length).First(idx =>
                max1 == FMath.ParameterOnLine(path1[FMath.Rem(idx, path1.Count)].position, refBone.src.position, refBone.dst.position));

            float min2 = Enumerable.Range(section2.First, section2.Length).Min(idx =>
                FMath.ParameterOnLine(path2[FMath.Rem(idx, path2.Count)].position, refBone.src.position, refBone.dst.position));
            float max2 = Enumerable.Range(section2.First, section2.Length).Max(idx =>
                FMath.ParameterOnLine(path2[FMath.Rem(idx, path2.Count)].position, refBone.src.position, refBone.dst.position));

            float idx2 = Enumerable.Range(section2.First, section2.Length).First(idx =>
                max2 == FMath.ParameterOnLine(path2[FMath.Rem(idx, path2.Count)].position, refBone.src.position, refBone.dst.position));

            System.Diagnostics.Debug.Assert(param[0] <= max1);
            System.Diagnostics.Debug.Assert(param[1] <= max1);
            System.Diagnostics.Debug.Assert(param[2] >= min2);
            System.Diagnostics.Debug.Assert(param[3] >= min2);

            float[] dParams = new[] {
                Math.Max(0, min1 - param[0]),
                Math.Max(0, min1 - param[1]),
                Math.Max(0, param[2] - max2),
                Math.Max(0, param[3] - max2),
            };

            float minDParam = dParams.Min();
            float boarderWidth = minDParam * FMath.Distance(refBone.src.position, refBone.dst.position);

            return boarderWidth;
        }

        // ボーンのdst方向にあるのがpatch2
        static void OverlayPatches(PatchSkeletalMesh patch1, PatchSkeletalMesh patch2, PatchSkeleton refSkeleton, PatchSection section1, PatchSection section2, PatchSkeletonBone bone, float border)
        {
            PatchSkeletonBone refBone = RefBone(refSkeleton, bone);
            float min1 = float.MaxValue;
            float max2 = float.MinValue;

            List<PatchVertex> path1 = GetPath(patch1);
            List<PatchVertex> path2 = GetPath(patch2);

            for (int i = section1.First; i < section1.First + section1.Length; i++)
            {
                var pt = path1[FMath.Rem(i, path1.Count)].position;
                float param = FMath.ParameterOnLine(pt, refBone.src.position, refBone.dst.position);
                if (float.IsNaN(param))
                    continue;
                min1 = Math.Min(min1, param);
            }
            for (int i = section2.First; i < section2.First + section2.Length; i++)
            {
                var pt = path2[FMath.Rem(i, path2.Count)].position;
                float param = FMath.ParameterOnLine(pt, refBone.src.position, refBone.dst.position);
                if (float.IsNaN(param))
                    continue;
                max2 = Math.Max(max2, param);
            }

            PointF x, y;
            BoneCoordinate(refBone, out x, out y);

            float boneLength = FMath.Distance(refBone.src.position, refBone.dst.position);
            float dparam = border / boneLength;
            float delta = (min1 - max2 - dparam) * boneLength;
            float dx = x.X * delta;
            float dy = x.Y * delta;

            // mesh2の頂点・制御点を平行移動する
            foreach (var v in patch2.mesh.vertices)
                v.position = new PointF(v.position.X + dx, v.position.Y + dy);
            foreach (var c in patch2.mesh.CopyControlPoints())
                patch2.mesh.TranslateControlPoint(c.position, new PointF(c.position.X + dx, c.position.Y + dy), false);
        }

        // 
        // Expand()
        //

        /// <summary>
        /// smesh1, smesh2の輪郭をずらして重ねる。輪郭に制御点をおいてARAPする
        /// curveOffset, curveLengthは切り口付近の補間に使う部分曲線
        /// </summary>
        static void Expand(
            PatchSkeletalMesh patch1, PatchSkeletalMesh patch2, PatchSkeleton refSkeleton,
            PatchSection section1, PatchSection section2, PatchSkeletonBone bone,
            float curveLength, float curveBuffer)
        {
            // メッシュを固定
            patch1.mesh.FreezeMesh(true);
            patch2.mesh.FreezeMesh(true);

            List<PatchVertex> rawPath1 = GetPath(patch1);
            List<PatchVertex> rawPath2 = GetPath(patch2);
            List<PointF> path1 = rawPath1.Select(v => v.position).ToList();
            List<PointF> path2 = rawPath2.Select(v => v.position).ToList();

            // 切り口に隣接する２曲線をそれぞれ取得
            var rawCurves1 = section2adjCurves(rawPath1, section1);
            var rawCurves2 = section2adjCurves(rawPath2, section2);
            if (rawCurves1 == null || rawCurves2 == null)
                return;

            var trimCurves1 = TrimCurves(rawPath1, rawCurves1, 0, curveLength);
            var trimCurves2 = TrimCurves(rawPath2, rawCurves2, 0, curveLength);

            PatchSkeletonBone refBone = RefBone(refSkeleton, bone);
            var sorted1 = GetSortedCurves(path1, trimCurves1, refBone);
            var sorted2 = GetSortedCurves(path2, trimCurves2, refBone);
            if (sorted1.Count != 2 || sorted2.Count != 2)
                return;

            // patch1の制御点
            patch1.mesh.ClearControlPoints();
            for (int i = 0; i < 2; i++)
                foreach (var p in sorted1[i])
                    patch1.mesh.AddControlPoint(p, p);
            patch1.mesh.BeginDeformation();

            // patch2の制御点
            patch2.mesh.ClearControlPoints();
            for (int i = 0; i < 2; i++)
                foreach (var p in sorted2[i])
                    patch2.mesh.AddControlPoint(p, p);
            patch2.mesh.BeginDeformation();

            // 第一要素、第二要素がそれぞれボーンにとって同じ側の切り口となるように並び替える
            // 切り口に近づく向きに点が並んでいる
            float h10 = CurveHeight(sorted1[0], refBone);
            float h11 = CurveHeight(sorted1[1], refBone);
            float h20 = CurveHeight(sorted2[0], refBone);
            float h21 = CurveHeight(sorted2[1], refBone);            
            var curve10 = sorted1[0].ToList();
            var curve11 = sorted1[1].ToList();
            var curve20 = sorted2[0].ToList();
            var curve21 = sorted2[1].ToList();

            // curveの重心を揃える
            PointF x, y;
            BoneCoordinate(refBone, out x, out y);
            
            float c0 = (h10 + h20) * 0.5f;
            float dy10 = c0 - h10;
            for (int i = 0; i < curve10.Count; i++)
                curve10[i] = new PointF(curve10[i].X + dy10 * y.X, curve10[i].Y + dy10 * y.Y);
            float dy11 = c0 - h20;
            for (int i = 0; i < curve20.Count; i++)
                curve20[i] = new PointF(curve20[i].X + dy11 * y.X, curve20[i].Y + dy11 * y.Y);

            float c1 = (h11 + h21) * 0.5f;
            float dy20 = c1 - h11;
            for (int i = 0; i < curve11.Count; i++)
                curve11[i] = new PointF(curve11[i].X + dy20 * y.X, curve11[i].Y + dy20 * y.Y);            
            float dy21 = c1 - h21;
            for (int i = 0; i < curve21.Count; i++)
                curve21[i] = new PointF(curve21[i].X + dy21 * y.X, curve21[i].Y + dy21 * y.Y);

            // curveをエルミート保管して繋げる 
            var ends0 = new[] { curve10[0], curve10.Last(), curve20[0], curve20.Last() };
            int min0, max0;
            FMath.GetMinElement(ends0, p => FMath.ParameterOnLine(p, refBone.src.position, refBone.dst.position), out min0);
            FMath.GetMinElement(ends0, p => -FMath.ParameterOnLine(p, refBone.src.position, refBone.dst.position), out max0);

            var ends1 = new[] { curve11[0], curve11.Last(), curve21[0], curve21.Last() };
            int min1, max1;
            FMath.GetMinElement(ends1, p => FMath.ParameterOnLine(p, refBone.src.position, refBone.dst.position), out min1);
            FMath.GetMinElement(ends1, p => -FMath.ParameterOnLine(p, refBone.src.position, refBone.dst.position), out max1);

            PointF[] es = new[] {
                ends0[min0],
                ends1[min1],
                ends0[max0],
                ends1[max1],
            };
            PointF[] vs = new[] {
                new PointF(curve10[0].X - curve10[curve10.Count - 1].X,  curve10[0].Y - curve10[curve10.Count - 1].Y),
                new PointF(curve11[0].X - curve11[curve11.Count - 1].X,  curve11[0].Y - curve11[curve11.Count - 1].Y),
                new PointF(-(curve20[0].X - curve20[curve20.Count - 1].X),  -(curve20[0].Y - curve20[curve20.Count - 1].Y)),
                new PointF(-(curve21[0].X - curve21[curve21.Count - 1].X),  -(curve21[0].Y - curve21[curve21.Count - 1].Y)),
            };
            float[] ps = new[] {
                FMath.ParameterOnLine(es[0], refBone.src.position, refBone.dst.position),
                FMath.ParameterOnLine(es[1], refBone.src.position, refBone.dst.position),
                FMath.ParameterOnLine(es[2], refBone.src.position, refBone.dst.position),
                FMath.ParameterOnLine(es[3], refBone.src.position, refBone.dst.position),
            };

            for (int i = 0; i < 2; i++)
            {
                var e1 = es[i];
                var v1 = vs[i];
                float param1 = ps[i];

                var e2 = es[i + 2];
                var v2 = vs[i + 2];
                float param2 = ps[i + 2];

                if (Math.Abs(param2 - param1) <= 1e-4)
                    continue;
                float paramRatio = 1 / (param2 - param1);

                for (int j = 0; j < sorted1[i].Count; j++)
                {
                    float param = FMath.ParameterOnLine(sorted1[i][j], refBone.src.position, refBone.dst.position);
                    float t = (param - param1) * paramRatio;
                    PointF to = FMath.HelmitteInterporate(e1, v1, e2, v2, t);
                    patch1.mesh.TranslateControlPoint(sorted1[i][j], to, false);
                }
                for (int j = 0; j < sorted2[i].Count; j++)
                {
                    float param = FMath.ParameterOnLine(sorted2[i][j], refBone.src.position, refBone.dst.position);
                    float t = (param - param1) * paramRatio;
                    PointF to = FMath.HelmitteInterporate(e1, v1, e2, v2, t);
                    patch2.mesh.TranslateControlPoint(sorted2[i][j], to, false);
                }
            }

            // 変形
            patch1.mesh.FlushDefomation();
            patch2.mesh.FlushDefomation();

            // 変形終了
            patch1.mesh.EndDeformation();
            patch2.mesh.EndDeformation();
        }

        static float CurveHeight(List<PointF> curve, PatchSkeletonBone bone)
        {
            PointF x, y;
            BoneCoordinate(bone, out x, out y);
            float h = 0;
            foreach (var p in curve)
                h += p.X * y.X + p.Y * y.Y;
            return h / curve.Count;
        }

        static List<PointF> v2p(List<PatchControlPoint> vertices)
        {
            var pts = new List<PointF>();
            foreach (var v in vertices)
            {
                pts.Add(v.position);
            }
            return pts;
        }
        static List<PointF> v2op(List<PatchControlPoint> vertices)
        {
            var pts = new List<PointF>();
            foreach (var v in vertices)
            {
                pts.Add(v.orgPosition);
            }
            return pts;
        }
        static List<PointF> v2p(List<PatchVertex> vertices)
        {
            var pts = new List<PointF>();
            foreach (var v in vertices)
            {
                pts.Add(v.position);
            }
            return pts;
        }
        static List<PointF> v2op(List<PatchVertex> vertices)
        {
            var pts = new List<PointF>();
            foreach (var v in vertices)
            {
                pts.Add(v.orgPosition);
            }
            return pts;
        }

        // 切り口に隣接する部分パス（２つ）を返す
        // この部分を引き伸ばしてメッシュを繋げる
        static Tuple<CharacterRange, CharacterRange> section2adjCurves(List<PatchVertex> path, PatchSection section)
        {
            if (section.Length <= 0 || path == null || path.Count < 5)
                return null;
            int start1 = FMath.Rem(section.First, path.Count);
            int end1 = start1 - 2;
            int start2 = FMath.Rem(section.First + section.Length - 1, path.Count);
            int end2 = start2 + 2;

            int part = path[start1].part;
            System.Diagnostics.Debug.Assert(part == path[start2].part);

            for (int i = 0; i < path.Count / 2; i++)
            {
                int idx1 = FMath.Rem(start1 - i, path.Count);
                int idx2 = FMath.Rem(start2 + i, path.Count);
                if (path[idx1].part != part || path[idx2].part != part)
                    break;
                end1 = idx1;
                end2 = idx2;
                if (FMath.Rem(end2 - end1, path.Count) <= 2)
                    break;
            }

            var r1 = rangeOnLoop(end1, start1, path.Count);
            var r2 = rangeOnLoop(start2, end2, path.Count);
            if (r1.Length <= 0 || r2.Length <= 0)
                return null;

            return new Tuple<CharacterRange, CharacterRange>(r1, r2);
        }

        static Tuple<CharacterRange, CharacterRange> TrimCurves(List<PatchVertex> path, Tuple<CharacterRange, CharacterRange> curves, float offset, float length)
        {
            return new Tuple<PatchSection, PatchSection>(TrimCurve(path, curves.Item1, offset, length), TrimCurve(path, curves.Item2, offset, length));
        }

        static CharacterRange TrimCurve(List<PatchVertex> path, CharacterRange curve, float offset, float length)
        {
            float total = 0;
            int start = 0;
            int cnt = 1;
            for (int i = curve.First; i < curve.First + curve.Length - 1; i++)
            {
                PointF p1 = path[FMath.Rem(i, path.Count)].position;
                PointF p2 = path[FMath.Rem(i + 1, path.Count)].position;
                total += FMath.Distance(p1, p2);
                if (total < offset)
                {
                    start++;
                    break;
                }
                if (total > length)
                    break;
                cnt++;
            }
            return new CharacterRange(FMath.Rem(curve.First + start, path.Count), cnt);
        }

        static CharacterRange rangeOnLoop(int start, int end, int loopCnt)
        {
            CharacterRange r = new CharacterRange();
            if (start < end)
                r = new CharacterRange(start, end - start + 1);
            if (start > end)
                r = new CharacterRange(start, end + loopCnt - start + 1);
            return r;
        }

        // 切り口に近づく向きに曲線の点をついかする
        static List<List<PointF>> GetSortedCurves(List<PointF> path, Tuple<CharacterRange, CharacterRange> ranges, PatchSkeletonBone baseBone)
        {
            List<List<PointF>> curves = new List<List<PointF>>();

            foreach (var range in new[] { ranges.Item1, ranges.Item2 })
            {
                var ls = new List<PointF>();
                for (int i = range.First; i < range.First + range.Length; i++)
                    ls.Add(path[FMath.Rem(i, path.Count)]);
                curves.Add(ls);
            }

            // 向きを揃える
            curves[1].Reverse();

            // ボーンに対する位置関係を揃える
            PointF pt = path[FMath.Rem(ranges.Item2.First, path.Count)];
            if (FMath.GetSide(pt, baseBone.src.position, baseBone.dst.position) < 0)
            {
                var ls = curves[0];
                curves[0] = curves[1];
                curves[1] = ls;
            }

            return curves;
        }
#endregion // Deform

        static PatchSkeletalMesh Combine(PatchSkeletalMesh smesh1, PatchSkeletalMesh smesh2, PatchSection section1, PatchSection section2)
        {
            throw new NotImplementedException();
        }

    }
}
