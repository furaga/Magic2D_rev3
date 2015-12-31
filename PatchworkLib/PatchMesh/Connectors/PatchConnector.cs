using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using FLib;
using PatchSection = System.Drawing.CharacterRange;


//
// TODO: 繋げるときはいったん話してからexpandして、それからもう一回SkeletonFitting()する
//

namespace PatchworkLib.PatchMesh
{
    /// 複数のパッチメッシュをリジッド変形可能な一つのパッチメッシュとして統合する
    /// 名称について.
    ///     PatchSkeletalMesh -> smesh
    ///     PatchMesh -> mesh
    ///     PatchMesh.Triangles -> triangle
    ///     PatchMeshの輪郭線 -> path (contourではない)
    public class PatchConnector
    {

        public static PatchSkeletalMesh Connect(List<PatchSkeletalMesh> smeshes, PatchSkeleton refSkeleton, PatchMeshRenderResources resources)
        {
            var overlapPairs = GetOverlappedPairs(smeshes, refSkeleton);
            if (overlapPairs.Count <= 0)
                return null;

            var newMesh = overlapPairs[0].Item1;
            List<PatchSkeletalMesh> aggregated = new List<PatchSkeletalMesh>();
            aggregated.Add(overlapPairs[0].Item1);

            int _orgCnt = overlapPairs.Count;

            for (int j = 0; j < overlapPairs.Count; j++)
            {
                for (int i = 0; i < overlapPairs.Count; i++)
                {
                    var pair = overlapPairs[i];
                    if (aggregated.Contains(pair.Item1) && !aggregated.Contains(pair.Item2))
                    {
                        newMesh = Connect(newMesh, pair.Item2, refSkeleton, resources);
                        aggregated.Add(pair.Item2);
                        overlapPairs.RemoveAt(i);
                        break;
                    }
                    else if (!aggregated.Contains(pair.Item1) && aggregated.Contains(pair.Item2))
                    {
                        newMesh = Connect(newMesh, pair.Item1, refSkeleton, resources);
                        aggregated.Add(pair.Item1);
                        overlapPairs.RemoveAt(i);
                        break;
                    }
                }
                System.Diagnostics.Debug.Assert(aggregated.Count == j + 2);
                System.Diagnostics.Debug.Assert(overlapPairs.Count == _orgCnt - j - 1);
            }

            return newMesh;
        }


        /// <summary>
        /// 2つの骨格つきメッシュをスケルトン情報を元に結合する
        /// 1. スケルトンに合わせて各メッシュをざっくり移動・ボーン方向にARAP
        /// 2. メッシュ同士が自然に繋がるように位置・角度・スケールを調整(fitting(), adjustposition())
        /// 3. 繋ぎ目が重なるようにARAP（expand()）
        /// 4. 新しいARAP可能なひとつのメッシュを生成(combine)                                                                                                                                                                                                                                                                                          /// </summary>
        /// (5. リソースの更新。これはここでやるべきなのだろうか？）
        /// </summary>
        public static PatchSkeletalMesh Connect(PatchSkeletalMesh smesh1, PatchSkeletalMesh smesh2, PatchSkeleton refSkeleton, PatchMeshRenderResources resources)
        {
            if (refSkeleton == null)
                return null;

            // メッシュ・骨格データはConnect()内で変更されうるのでコピーしたものを使う
            PatchSkeletalMesh smesh1_t = PatchSkeletalMesh.Copy(smesh1);
            PatchSkeletalMesh smesh2_t = PatchSkeletalMesh.Copy(smesh2);
#if DEBUG
            PatchSkeletalMeshRenderer.ToBitmap(smesh1_t, smesh1_t.sections, alignment: true).Save("smesh1_t.png");
            PatchSkeletalMeshRenderer.ToBitmap(smesh2_t, smesh2_t.sections, alignment: true).Save("smesh2_t.png");
#endif
            var refSkeleton_t = PatchSkeleton.Copy(refSkeleton);

            // smesh1, smesh2で同じボーンを共有している（結合すべき）切り口を探す
            // これらの切り口の付近を変形することでメッシュを繋げる
            PatchSection section1;
            PatchSection section2;
            PatchSkeletonBone crossingBone;

            bool canConnect = CanConnect(new List<PatchSkeletalMesh>() { smesh1, smesh2 }, refSkeleton);
            bool canConnect_t = CanConnect(new List<PatchSkeletalMesh>() { smesh1_t, smesh2_t }, refSkeleton_t);

            bool found = FindConnectingSections(smesh1_t, smesh2_t, refSkeleton_t, out section1, out section2, out crossingBone);
            if (!found)
                return null;
#if _DEBUG
            PatchSkeletalMeshRenderer.ToBitmap(smesh1_t, new List<CharacterRange>() { section1 }).Save("output/3_mesh1_t_seciton1.png");
            PatchSkeletalMeshRenderer.ToBitmap(smesh2_t, new List<CharacterRange>() { section2 }).Save("output/4_mesh2_t_section2.png");
#endif

            // ２つのsmeshが重なるように位置調整およびARAP変形をする
            smesh1_t.mesh.BeginDeformation();
            smesh2_t.mesh.BeginDeformation();
            Deform(smesh1_t, smesh2_t, refSkeleton_t, section1, section2, crossingBone);
            smesh2_t.mesh.EndDeformation();
            smesh1_t.mesh.EndDeformation();
#if _DEBUG
            PatchSkeletalMeshRenderer.ToBitmap(smesh1_t).Save("output/6_mesh1_t_deformed.png");
            PatchSkeletalMeshRenderer.ToBitmap(smesh2_t).Save("output/7_mesh2_t_deformed.png");
#endif

            // ２つの変形済みのsmeshを１つのsmeshに結合して、ARAPできるようにする
            var combinedSMesh = Combine(smesh1_t, smesh2_t, section1, section2);
#if _DEBUG
            PatchSkeletalMeshRenderer.ToBitmap(combinedSMesh, combinedSMesh.sections).Save("output/8_conbinedMesh.png");
#endif

            if (resources != null)
            {
                List<string> textureKeys = resources.GetResourceKeyByPatchMesh(smesh1.mesh);
                textureKeys.AddRange(resources.GetResourceKeyByPatchMesh(smesh2.mesh));

                foreach (var key in textureKeys)
                {
                    string patchKey = key.Split(':').Last();
                    string newKey = PatchMeshRenderResources.GenerateResourceKey(combinedSMesh.mesh, patchKey);
                    // TODO: テクスチャはコピーしたほうが良い？
                    resources.Add(newKey, resources.GetTexture(key));
                }
            }

            return combinedSMesh;
        }

        static List<Tuple<PatchSkeletalMesh, PatchSkeletalMesh>> GetOverlappedPairs(List<PatchSkeletalMesh> smeshes, PatchSkeleton refSkeleton)
        {
            Dictionary<PatchSkeletonBone, List<PatchSkeletalMesh>> overlaps = new Dictionary<PatchSkeletonBone, List<PatchSkeletalMesh>>();
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
                        {
                            //                       System.Diagnostics.Debug.Assert(false);
                            return new List<Tuple<PatchSkeletalMesh,PatchSkeletalMesh>>();
                        }
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

        public static bool CanConnect(List<PatchSkeletalMesh> smeshes, PatchSkeleton refSkeleton)
        {
            if (smeshes == null || refSkeleton == null || smeshes.Count <= 1)
                return false;

            // 1) ボーンの重なりが最大2
            // 2) 重なっているボーンで全てのメッシュが連結される
            // -FindConnectingSections() == true

            var overlapPairs = GetOverlappedPairs(smeshes, refSkeleton);

            HashSet<PatchSkeletalMesh> overlapSMeshes = new HashSet<PatchSkeletalMesh>();

            foreach (var pair in overlapPairs)
            {
                overlapSMeshes.Add(pair.Item1);
                overlapSMeshes.Add(pair.Item2);
            }

            if (overlapSMeshes.Count != smeshes.Count)
            {
                return false;
            }

            foreach (var pair in overlapPairs)
            {
                PatchSection section1;
                PatchSection section2;
                PatchSkeletonBone crossingBone;
                bool found = FindConnectingSections(pair.Item1, pair.Item2, refSkeleton, out section1, out section2, out crossingBone);

                if (!found)
                {
                    return false;
                }
            }

            return true;
        }


        //-----------------------------------------------------------------------------------
        // FindConnectingSections()
        //-----------------------------------------------------------------------------------

        /// <summary>
        /// skl内の同一のボーンと交差していて、なおかつ向きが逆の切り口を探す
        /// </summary>
        private static bool FindConnectingSections(
            PatchSkeletalMesh smesh1, PatchSkeletalMesh smesh2, PatchSkeleton skl, 
            out PatchSection section1, out PatchSection section2, out PatchSkeletonBone crossingBone)
        {
            section1 = new PatchSection(0, -1);
            section2 = new PatchSection(0, -1);
            crossingBone= null;

            if (skl == null)
                return false;

            if (smesh1 == null || smesh2 == null)
                return false;

            foreach (var bone in skl.bones)
            {
                List<PatchSection> sections1, sections2;
                List<float> dir1, dir2;

                // 各切り口がboneと交差しているか
                if (!FindCrossingSection(smesh1, bone, out sections1, out dir1))
                    continue;
                if (!FindCrossingSection(smesh2, bone, out sections2, out dir2))
                    continue;

                for (int i = 0; i < sections1.Count; i++)
                {
                    for (int j = 0; j < sections2.Count; j++)
                    {
                       // ２つの切り口が逆向きか
                        if (dir1[i] * dir2[j] >= 0)
                            continue;
                        section1 = sections1[i];
                        section2 = sections2[j];
                        crossingBone = bone;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// meshのboneと交差している切り口を1つ返す
        /// dir は交差の仕方。切り口の逆なら符号の異なる値を返す
        /// </summary>
        static bool FindCrossingSection(PatchSkeletalMesh smesh, PatchSkeletonBone refBone, out List<PatchSection> sections, out List<float> dirs)
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
                        float ratio = 1f /  FLib.FMath.Distance(bone.src.position, bone.dst.position);
                        PointF boneDir = new PointF(bone.src.position.X - bone.dst.position.X, bone.src.position.Y - bone.dst.position.Y);
                        float sampleX = crossPoint.X + boneDir.X * ratio;
                        float sampleY = crossPoint.Y + boneDir.Y * ratio;
                        bool inMesh = FLib.FMath.IsPointInPolygon(new PointF(sampleX, sampleY), GetPath(smesh).Select(v => v.position).ToList());
                        if (inMesh)
                        {
                            dirs.Add(-1);
                        }
                        else
                        {
                            dirs.Add(1);
                        }
                    }
                }
            }

            if (sections.Count >= 1)
                return true;

            return false;
        }


        //-----------------------------------------------------------------------------------
        // Deform()
        //-----------------------------------------------------------------------------------
        
        /// <summary>
        /// 切り口付近で２つのsmeshが重なるように変形する
        /// </summary>
        private static void Deform(PatchSkeletalMesh smesh1, PatchSkeletalMesh smesh2, PatchSkeleton skl, PatchSection section1, PatchSection section2, PatchSkeletonBone crossingBone)
        {
            if (smesh1 == null || smesh2 == null)
                return;

            // 各メッシュを大雑把にスケルトンに合わせる
            PatchSkeletonFitting.Fitting(smesh1, skl);
            PatchSkeletonFitting.Fitting(smesh2, skl);
#if _DEBUG
            PatchSkeletalMeshRenderer.ToBitmap(smesh1, new List<PatchSection>() { section1 }).Save("output/5_1_mesh1_fitting.png");
            PatchSkeletalMeshRenderer.ToBitmap(smesh2, new List<PatchSection>() { section2 }).Save("output/5_2_mesh2_fitting.png");
#endif

            // サイズの修正は手動でやる
            // 回転はFitting()でやってるから必要ない            
            
            // 位置の調整
            AdjustPosition(smesh1, smesh2, skl, section1, section2, crossingBone);
#if _DEBUG
            PatchSkeletalMeshRenderer.ToBitmap(smesh1, new List<PatchSection>() { section1 }).Save("output/5_3_mesh1_AdjustPosition.png");
            PatchSkeletalMeshRenderer.ToBitmap(smesh2, new List<PatchSection>() { section2 }).Save("output/5_4_mesh2_AdjustPosition.png");
#endif

            // メッシュを伸ばして繋げる
            Expand(smesh1, smesh2, skl, section1, section2, crossingBone);
#if _DEBUG
            PatchSkeletalMeshRenderer.ToBitmap(smesh1, new List<PatchSection>() { section1 }).Save("output/5_5_mesh1_ExpandPatches.png");
            PatchSkeletalMeshRenderer.ToBitmap(smesh2, new List<PatchSection>() { section2 }).Save("output/5_6_mesh2_ExpandPatches.png");
#endif
        }


        // 
        // AdjustPosition()
        //

        static void AdjustPosition(
            PatchSkeletalMesh smesh1, PatchSkeletalMesh smesh2, PatchSkeleton skl, 
            PatchSection section1, PatchSection section2, PatchSkeletonBone bone)
        {
            PatchSkeletonBone refBone = null;
            foreach (var b in skl.bones)
            {
                if (bone == b)
                {
                    refBone = b;
                    break;
                }
            }

            if (refBone == null)
                return;

            // 切り口の中心とボーンの軸とのずれ（ボーンと垂直な方向について）
            float height1 = SectionHeight(smesh1, section1, refBone);
            float height2 = SectionHeight(smesh2, section2, refBone);

            // ボーンと水平なベクトルと垂直なベクトルをそれぞれx, yとして取得
            PointF x, y;
            CalcBoneCoordinate(refBone, out x, out y);

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

        static float SectionHeight(PatchSkeletalMesh mesh, PatchSection section, PatchSkeletonBone b)
        {
            List<PointF> path = GetPath(mesh).Select(v => v.position).ToList();

            var curves = SectionToAdjuscentCurves(path, section, 5, 30);

            if (path == null || curves == null || b == null)
                return 0;

            if (curves.Item1 == null || curves.Item2 == null)
                return 0;

            float height = 0;

            float boneLen = FMath.Distance(b.src.position, b.dst.position);
            if (boneLen <= 1e-4)
                return 0;

            PointF x, y;
            CalcBoneCoordinate(b, out x, out y);

            // 各セグメントのボーンからのズレを求める
            int cnt = Math.Min(curves.Item1.Length, curves.Item2.Length);
            for (int i = 0; i < cnt; i++)
            {
                int idx1 = curves.Item1.First + curves.Item1.Length - 1 - i;
                int idx2 = curves.Item2.First + i;
                while (idx1 < 0)
                    idx1 += path.Count;
                while (idx2 < 0)
                    idx2 += path.Count;
                PointF pt1 = path[idx1 % path.Count];
                PointF pt2 = path[idx2 % path.Count];
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

        static void CalcBoneCoordinate(PatchSkeletonBone b, out PointF x, out PointF y)
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


        
        // 
        // Expand()
        //

        /// <summary>
        /// smesh1, smesh2の輪郭をずらして重ねる。輪郭に制御点をおいてARAPする
        /// </summary>
        static void Expand(PatchSkeletalMesh smesh1, PatchSkeletalMesh smesh2, PatchSkeleton skl, PatchSection section1, PatchSection section2, PatchSkeletonBone bone)
        {
            List<PatchVertex> rawPath1 = GetPath(smesh1);
            List<PatchVertex> rawPath2 = GetPath(smesh2);
            List<PointF> path1 = rawPath1.Select(v => v.position).ToList();
            List<PointF> path2 = rawPath2.Select(v => v.position).ToList();

            //
            // 輪郭を変形できるように制御点を作り直す
            //

            // smesh1
            smesh1.mesh.ClearControlPoints();
            foreach (var v in rawPath1)
                smesh1.mesh.AddControlPoint(v.position, v.orgPosition);
            smesh1.mesh.BeginDeformation();

            // smesh2
            smesh2.mesh.ClearControlPoints();
            foreach (var v in rawPath2)
                smesh2.mesh.AddControlPoint(v.position, v.orgPosition);
            smesh2.mesh.BeginDeformation();


            //
            // 切り口に隣接する２曲線を各切り口について取得し、これらが重なるように輪郭をずらす
            //

            // 切り口に隣接する２曲線をそれぞれ取得
            var rawCurves1 = SectionToAdjuscentCurves(path1, section1, 5, 30);
            var rawCurves2 = SectionToAdjuscentCurves(path2, section2, 5, 30);
            if (rawCurves1 == null || rawCurves2 == null)
                return;

            PatchSkeletonBone refBone = null;
            foreach (var b in skl.bones)
            {
                if (bone == b)
                {
                    refBone = b;
                    break;
                }
            }

            // curves1, curves2の第一要素、第二要素がそれぞれ向かい合う（ボーンにとって同じ側の）切り口となるように並び替える
            var curves1 = GetSortedCurves(path1, rawCurves1, refBone);
            var curves2 = GetSortedCurves(path2, rawCurves2, refBone);
            if (curves1.Count != 2 || curves2.Count != 2)
                return;

            // curves1, curves2の移動履歴を記録
            List<Tuple<PointF, PointF>> move1 = new List<Tuple<PointF, PointF>>();
            List<Tuple<PointF, PointF>> move2 = new List<Tuple<PointF, PointF>>();

            // 対応する曲線間で２点がかぶる（同じ座標になる）ように変形。
            for (int i = 0; i < 2; i++)
            {
                var p1 = curves1[i].First();
                var v1 = new PointF(p1.X - curves1[i].Last().X, p1.Y - curves1[i].Last().Y);
                var p2 = curves2[i].First();
                var v2 = new PointF(curves2[i].Last().X - p2.X, curves2[i].Last().Y - p2.Y);

                // ２点かぶらせる
                int cnt = curves1[i].Count + curves2[i].Count - 2;
                if (cnt <= 1)
                    continue;

                for (int j = 0; j < curves1[i].Count; j++)
                {
                    PointF to = FMath.HelmitteInterporate(p1, v1, p2, v2, (float)j / (cnt - 1));
                    if (j == curves1[i].Count - 1)
                        move1.Add(new Tuple<PointF, PointF>(curves1[i][j], to));
                    smesh1.mesh.TranslateControlPoint(curves1[i][j], to, false);
                }
                for (int j = 0; j < curves2[i].Count; j++)
                {
                    PointF to = FMath.HelmitteInterporate(p1, v1, p2, v2, (float)(-j + cnt - 1) / (cnt - 1));
                    if (j == curves2[i].Count - 1)
                        move2.Add(new Tuple<PointF, PointF>(curves2[i][j], to));
                    smesh2.mesh.TranslateControlPoint(curves2[i][j], to, false);
                }
            }

            //
            // 各曲線の動きに合わせて切り口を動かす
            //
            List<PointF> sections1 = new List<PointF>();
            for (int i = section1.First + 1; i < section1.First + section1.Length - 1; i++)
                sections1.Add(path1[FMath.Rem(i, path1.Count)]);
            List<PointF> newSection1 = ARAPDeformation.ARAPDeformation.Deform(sections1, move1);
            if (newSection1.Count == sections1.Count)
                for (int i = 0; i < newSection1.Count; i++)
                    smesh1.mesh.TranslateControlPoint(sections1[i], newSection1[i], false);

            List<PointF> sections2 = new List<PointF>();
            for (int i = section2.First + 1; i < section2.First + section2.Length - 1; i++)
                sections2.Add(path2[FMath.Rem(i, path2.Count)]);
            List<PointF> newSection2 = ARAPDeformation.ARAPDeformation.Deform(sections2, move2);
            if (newSection2.Count == sections2.Count)
                for (int i = 0; i < newSection2.Count; i++)
                    smesh2.mesh.TranslateControlPoint(sections2[i], newSection2[i], false);

            //
            // 変形
            //
            smesh1.mesh.FlushDefomation();
            smesh2.mesh.FlushDefomation();

            //
            // 変形終了
            //
            smesh1.mesh.EndDeformation();
            smesh2.mesh.EndDeformation();
        }

        // 切り口に隣接する部分パス（２つ）を返す
        // この部分を引き伸ばしてメッシュを繋げる
        static Tuple<CharacterRange, CharacterRange> SectionToAdjuscentCurves(List<PointF> path, PatchSection section, int maxPtNum, float maxAngle)
        {
            if (section.Length <= 0 || path == null || path.Count < 5 || maxPtNum < 3)
                return null;

            float cos = (float)Math.Cos(maxAngle);

            // １つ目の部分パス

            int start1 = section.First;
            while (start1 < 0)
                start1 += path.Count;
            start1 = start1 % path.Count;

            int end1 = start1 - 2;

            for (int i = 0; i < maxPtNum - 2; i++)
            {
                int idx = (start1 - 2 - i + path.Count) % path.Count;
                var curve = new List<PointF>();
                for (int j = idx; j < idx + 3; j++)
                    curve.Add(path[FMath.Rem(j, path.Count)]);
                if (FMath.GetAngleCos(curve) < cos)
                    break;
                end1 = idx;
            }

            CharacterRange r1 = new CharacterRange();
            if (start1 > end1)
                r1 = new CharacterRange(end1, start1 - end1 + 1);
            if (start1 < end1)
                // 始点と終点をまたがっている場合
                r1 = new CharacterRange(end1, start1 + path.Count - end1 + 1);

            if (r1.Length <= 0)
                return null;

            // ２つ目の部分パス

            int start2 = section.First + section.Length - 1;
            while (start2 < 0)
                start2 += path.Count;
            start2 = start2 % path.Count;

            int end2 = start2 + 2;

            for (int i = 0; i < maxPtNum - 2; i++)
            {
                int idx = (start2 + i) % path.Count;
                var curve = path.Skip(idx).Take(3).ToList();
                if (FMath.GetAngleCos(curve) < cos)
                    break;
                end2 = start2 + i + 2;
            }

            CharacterRange r2 = new CharacterRange();

            if (start2 < end2)
                r2 = new CharacterRange(start2, end2 - start2 + 1);
            if (start2 > end2)
                r2 = new CharacterRange(start2, end2 + path.Count - start2 + 1);

            if (r2.Length <= 0)
                return null;

            return new Tuple<CharacterRange, CharacterRange>(r1, r2);
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


        //--------------------------------------------------------------------------
        // Combine()
        //--------------------------------------------------------------------------

        /// <summary>
        /// 2つのメッシュを統合して１つのARAP可能なメッシュを作成する
        /// TODO: さすがに関数にわけるべき
        /// </summary>
        static PatchSkeletalMesh Combine(PatchSkeletalMesh smesh1, PatchSkeletalMesh smesh2, PatchSection section1, PatchSection section2)
        {
            //
            // Meshを作成
            //
            
            List<PatchVertex> vertices = CombineVertices(smesh1.mesh.vertices, smesh2.mesh.vertices);

            // 頂点から各種インデックスへの辞書を作っておく
            Dictionary<PointF, int> p2i = new Dictionary<PointF, int>();
            for (int i = 0; i < vertices.Count; i++)
                p2i[vertices[i].position] = i;
            Dictionary<PointF, int> pt2part = vertices.ToDictionary(v => v.position, v => v.part);

            Dictionary<int, int> part2part_1 = new Dictionary<int, int>(); // smesh1の各パートが新しいメッシュのどのパートになるか
            foreach (var v in smesh1.mesh.vertices)
                part2part_1[v.part] = pt2part[v.position];

            Dictionary<int, int> part2part_2 = new Dictionary<int, int>(); // smesh2の各パートが新しいメッシュのどのパートになるか
            foreach (var v in smesh2.mesh.vertices)
                part2part_2[v.part] = pt2part[v.position];

            List<PatchControlPoint> controlPoints = CombineControlPoints(smesh1.mesh.CopyControlPoints(), smesh2.mesh.CopyControlPoints(), part2part_1, part2part_2);
            List<PatchTriangle> triangles = CombineTriangles(smesh1.mesh.triangles, smesh2.mesh.triangles, smesh1.mesh.vertices, smesh2.mesh.vertices, p2i);
            List<int> path = CombinePath(smesh1.mesh.pathIndices, smesh2.mesh.pathIndices, smesh1.mesh.vertices, smesh2.mesh.vertices, section1, section2, p2i);

            PatchMesh rawMesh = new PatchMesh(vertices, controlPoints, triangles, path);

            //
            // 骨格を統合
            //

            PatchSkeleton skl = CombineSkeleton(smesh1.skl, smesh2.skl);


            //
            // 切り口を統合. 
            //
            List<PatchSection> sections = CombineSections(
                smesh1.sections, smesh2.sections, 
                section1, section2, 
                smesh1.mesh.vertices, smesh2.mesh.vertices,
                smesh1.Mesh.pathIndices, smesh2.Mesh.pathIndices,
                path, p2i);

            //
            // SkeletalMeshを作成. 
            //
            PatchSkeletalMesh newMesh = new PatchSkeletalMesh(rawMesh, skl, sections);

            return newMesh;
        }

        private static List<PatchVertex> CombineVertices(List<PatchVertex> vertices1, List<PatchVertex> vertices2)
        {
            // 各頂点に登録するテクスチャ座標（テクスチャごと）を計算
            Dictionary<PointF, Dictionary<string, PointF>> pt2texcoords = new Dictionary<PointF, Dictionary<string, PointF>>();
            foreach (var v in vertices1)
            {
                if (!pt2texcoords.ContainsKey(v.position))
                    pt2texcoords[v.position] = new Dictionary<string, PointF>();
                foreach (var kv in v.CopyTexcoordDict())
                    pt2texcoords[v.position][kv.Key] = kv.Value;
            }
            foreach (var v in vertices2)
            {
                if (!pt2texcoords.ContainsKey(v.position))
                    pt2texcoords[v.position] = new Dictionary<string, PointF>();
                foreach (var kv in v.CopyTexcoordDict())
                    pt2texcoords[v.position][kv.Key] = kv.Value;
            }


            // 各頂点が属する（ARAPにおける）部位を計算
            Dictionary<PointF, int> pt2part = new Dictionary<PointF, int>();
            Dictionary<PointF, string> pt2partKey = new Dictionary<PointF, string>();
            Dictionary<string, string> samePart = new Dictionary<string, string>();
            foreach (var v in vertices1)
            {
                string key = "1:" + v.part;
                pt2partKey[v.position] = key;
            }
            foreach (var v in vertices2)
            {
                string key = "2:" + v.part;
                if (pt2partKey.ContainsKey(v.position))
                    samePart[key] = pt2partKey[v.position];
                else
                    pt2partKey[v.position] = key;
            }
            Dictionary<string, int> partKey2part = new Dictionary<string, int>();
            for (int i = 0; i < pt2partKey.Count; i++)
            {
                string partKey = pt2partKey.Values.ElementAt(i);
                while (samePart.ContainsKey(partKey))
                    partKey = samePart[partKey];
                if (!partKey2part.ContainsKey(partKey))
                    partKey2part[partKey] = partKey2part.Count;
                pt2part[pt2partKey.Keys.ElementAt(i)] = partKey2part[partKey];
            }

            // 結合後の頂点集合を生成
            List<PatchVertex> vertices = new List<PatchVertex>();
            foreach (var kv in pt2texcoords)
                vertices.Add(new PatchVertex(kv.Key, pt2part[kv.Key], kv.Value));

            return vertices;
        }

        private static List<PatchControlPoint> CombineControlPoints(List<PatchControlPoint> controlPoints1, List<PatchControlPoint> controlPoints2, 
            Dictionary<int, int> part2part_1, Dictionary<int, int> part2part_2)
        {
            List<PatchControlPoint> controlPoints = new List<PatchControlPoint>();

            foreach (var c in controlPoints1)
                controlPoints.Add(new PatchControlPoint(c.position, part2part_1[c.part]));
            foreach (var c in controlPoints2)
                controlPoints.Add(new PatchControlPoint(c.position, part2part_2[c.part]));

            return controlPoints;
        }

        private static List<PatchTriangle> CombineTriangles(List<PatchTriangle> triangles1, List<PatchTriangle> triangles2, List<PatchVertex> vertices1, List<PatchVertex> vertices2, Dictionary<PointF, int> p2i)
        {
            List<PatchTriangle> triangles = new List<PatchTriangle>();
            foreach (var t in triangles1)
            {
                int i0 = p2i[vertices1[t.Idx0].position];
                int i1 = p2i[vertices1[t.Idx1].position];
                int i2 = p2i[vertices1[t.Idx2].position];
                string textureKey = t.TextureKey;
                triangles.Add(new PatchTriangle(i0, i1, i2, textureKey));
            }
            foreach (var t in triangles2)
            {
                int i0 = p2i[vertices2[t.Idx0].position];
                int i1 = p2i[vertices2[t.Idx1].position];
                int i2 = p2i[vertices2[t.Idx2].position];
                string textureKey = t.TextureKey;
                triangles.Add(new PatchTriangle(i0, i1, i2, textureKey));
            }
            return triangles;
        }


        private static List<int> CombinePath(List<int> path1, List<int> path2,
            List<PatchVertex> vertices1,
            List<PatchVertex> vertices2,
            PatchSection section1, PatchSection section2,
            Dictionary<PointF, int> p2i)
        {
            List<int> path = new List<int>();

            HashSet<int> sectionIndices1 = new HashSet<int>();
            for (int i = section1.First; i < section1.First + section1.Length; i++)
                sectionIndices1.Add(i);

            HashSet<int> sectionIndices2 = new HashSet<int>();
            for (int i = section2.First; i < section2.First + section2.Length; i++)
                sectionIndices2.Add(i);

            HashSet<PointF> pathPoints = new HashSet<PointF>();

            // 切り口の端からパスを登録していく.  切り口の両端は必ずpathに含まれている。と思う
            for (int _i = section1.First + section1.Length; _i != section1.First + 1; _i = FMath.Rem(_i + 1, path1.Count))
            {
                int i = path1[_i];
                if (sectionIndices1.Contains(i))
                    continue;
                path.Add(p2i[vertices1[i].position]);
                pathPoints.Add(vertices1[i].position);
            }

            PointF path1_preendPt = vertices1[path1[FMath.Rem(section1.First - 1, path1.Count)]].position;
            PointF path1_endPt = vertices1[path1[FMath.Rem(section1.First, path1.Count)]].position;

            int path2_start = -1;
            for (int i = 0; i < path2.Count; i++)
            {
                if (vertices2[path2[i]].position == path1_endPt)
                {
                    path2_start = i;
                    break;
                }
            }

            if (path2_start < 0)
                return new List<int>();

            int path2_poststart = FMath.Rem(path2_start + 1, path2.Count);
            PointF path2_poststartPt = vertices2[path2[path2_poststart]].position;
            int dir = 1;
            if (path2_poststartPt == path1_preendPt)
                dir = -1;

            for (int i = path2_start + dir; ; i += dir)
            {
                PointF pt = vertices2[path2[FMath.Rem(i, path2.Count)]].position;
                if (path.Contains(p2i[pt]))
                    break;
                path.Add(p2i[pt]);
            }

            return path;
        }

        private static PatchSkeleton CombineSkeleton(PatchSkeleton skl1, PatchSkeleton skl2)
        {
            PatchSkeleton skl = new PatchSkeleton();

            // 関節を追加
            foreach (var j in skl1.joints)
            {
                skl.joints.Add(new PatchSkeletonJoint(j.name, j.position));
            }
            foreach (var j in skl2.joints)
            {
                if (!skl.joints.Any(_j => _j.name == j.name))
                {
                    skl.joints.Add(new PatchSkeletonJoint(j.name, j.position));
                }
            }

            // ボーンを追加
            foreach (var b in skl1.bones)
            {
                if (skl.joints.Any(j => j.name == b.src.name) && skl.joints.Any(j => j.name == b.dst.name))
                {
                    var newBone = new PatchSkeletonBone(
                        skl.joints.First(j => j.name == b.src.name),
                        skl.joints.First(j => j.name == b.dst.name));
                    skl.bones.Add(newBone);
                }
            }
            foreach (var b in skl2.bones)
            {
                if (skl.joints.Any(j => j.name == b.src.name) && skl.joints.Any(j => j.name == b.dst.name))
                {
                    var newBone = new PatchSkeletonBone(
                        skl.joints.First(j => j.name == b.src.name),
                        skl.joints.First(j => j.name == b.dst.name));
                    if (!skl.bones.Contains(newBone))
                        skl.bones.Add(newBone);
                }
            }

            return skl;
        }

        private static List<PatchSection> CombineSections(
            List<PatchSection> sections1, List<PatchSection> sections2,
            PatchSection exceptSection1, PatchSection exceptSection2,
            List<PatchVertex> vertices1, List<PatchVertex> vertices2,
            List<int> path1, List<int> path2,
            List<int> combinedPath,
            Dictionary<PointF, int> p2i)
        {
            List<PatchSection> sections = new List<PatchSection>();
            foreach (var sec in sections1)
            {
                if (sec == exceptSection1)
                    continue;

                // 切り口の端点が新しいメッシュ内でどこにあるか
                sections.Add(GetMappedSection(sec, vertices1, path1, combinedPath, p2i));
          }
            foreach (var sec in sections2)
            {
                if (sec == exceptSection2)
                    continue;

                // 切り口の端点が新しいメッシュ内でどこにあるか
                sections.Add(GetMappedSection(sec, vertices2, path2, combinedPath, p2i));
}
            return sections;
        }

        static PatchSection GetMappedSection(PatchSection orgsection, List<PatchVertex> vertices, List<int> orgpath, List<int> combinedPath, Dictionary<PointF, int> p2i)
        {
            int idx0 = orgpath[FMath.Rem(orgsection.First, orgpath.Count)];
            PointF p0 = vertices[idx0].position;
            int newIdx0 = combinedPath.IndexOf(p2i[p0]);

            int idx1 = orgpath[FMath.Rem(orgsection.First + orgsection.Length - 1, orgpath.Count)];
            PointF p1 = vertices[idx1].position;
            int newIdx1 = combinedPath.IndexOf(p2i[p1]);

            if (newIdx0 > newIdx1)
                FMath.Swap(ref newIdx0, ref newIdx1);

            PatchSection newSection = new PatchSection(newIdx0, newIdx1 - newIdx0 + 1);

            return newSection;
        }


    }
}
