/*
 * 
 * 1) つなぎ方が汚い
 * DecoBrush(http://gfx.cs.princeton.edu/pubs/Lu_2014_DDS/Lu_2014_DDS.pdf)ないしBiggerPictureの合成を参考にConnectorを改良
 * いろいろ工夫しているみたいなので、できるだけ取り入れる
 * 時間はかかるが大事なことなので時間をかける
 * Connect時に完全に新しいメッシュにすべき
 * 
 * 2) メッシュの一部を切り口方向に伸ばしたい.縮ませたい
 * ボーン選択モードにして、スライダーで縮小
 * 
 * 3) 繋げられない時は重ねる
 * 
 * 4) スケルトンの形によるパッチの自動選択
 * 最初は単にconnectivePatchListをソートするだけ
 * 
 */

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

using SharpDX;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D;
using SharpDX.Windows;
using PatchworkLib.PatchMesh;
using FLib.SharpDX;
using FLib;
using Buffer = SharpDX.Direct3D11.Buffer;
using Device = SharpDX.Direct3D11.Device;
using MapFlags = SharpDX.Direct3D11.MapFlags;
using DXColor = SharpDX.Color;

namespace Patchwork
{
    public partial class Form1 : Form
    {
        class SaveObjects
        {
            public float cameraX, cameraY, cameraZ;
            public PatchSkeleton refSkeleton;
            //            public Dictionary<string, List<string>> texturePathKeys = new Dictionary<string,List<string>>();
            //            public Dictionary<string, RenderQuery> renderQueryPool = new Dictionary<string, RenderQuery>();
            //            public List<RenderQuery> renderPatchQueries = new List<RenderQuery>();
        }

        class RenderQuery
        {
            public PatchSkeletalMesh patch;
            public PatchTree patchTree;
            public List<string> patchKeys;
            public RenderQuery(PatchSkeletalMesh patch, PatchTree patchTree, List<string> patchKeys)
            {
                this.patch = patch;
                this.patchKeys = new List<string>(patchKeys);
            }
        }

        /// 現在のパッチがどのように生成されたか。パッチを分解したりするときに使う
        class PatchTree
        {
            readonly string patchKey = null;
            readonly PatchTree subtree1 = null;
            readonly PatchTree subtree2 = null;
            public PatchTree(string patchKey)
            {
                this.patchKey = patchKey;
            }
            public PatchTree(PatchTree t1, PatchTree t2)
            {
                this.subtree1 = t1;
                this.subtree2 = t2;
            }
        }


        // 描画系
        PatchMeshRenderer renderer;
        PatchMeshRenderResources resources = new PatchMeshRenderResources();
        public Dictionary<SharpDX.Direct3D11.Texture2D, Bitmap> tex2bmp = new Dictionary<SharpDX.Direct3D11.Texture2D, Bitmap>();
        Vector3 cameraPosition = new Vector3(400, 400, -2000);


        // 変形先の骨格
        PatchSkeleton refSkeleton;

        // インタラクション
        List<RenderQuery> selectingQueries = new List<RenderQuery>();

        Dictionary<string, RenderQuery> renderQueryPool = new Dictionary<string, RenderQuery>();
        List<RenderQuery> renderQueries = new List<RenderQuery>();

        int combinedCount = 0;

        //---------------------------------------------------------

        public Form1()
        {
            InitializeComponent();

            FLib.FLib.Initalize();
            List<PointF> input = new List<PointF>();
            List<PointF> outVert = new List<PointF>();
            List<int> indices = new List<int>();
            for (int i = 0; i < 100; i++)
            {
                input.Add(new PointF(
                    500 * (float)Math.Cos(Math.PI * 2 * i / 100),
                    500 * (float)Math.Sin(Math.PI * 2 * i / 100)));
            }
            Triangle.Triangulate(input, outVert, indices, new Triangle.Parameters(false));
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            //            FLib.FileManager.OpenExplorer("output");

            canvas.AllowDrop = true;

            renderer = new PatchMeshRenderer(canvas.Handle, canvas.ClientSize, true);

            // Magic2Dで作ったセグメントをロードしてパッチに変換
            Dictionary<string, PatchSkeletalMesh> dict;
            Dictionary<string, Bitmap> bitmaps = new Dictionary<string, Bitmap>();

//            dict = Magic2D.SegmentToPatch.LoadPatches("../../../../..", "Patchwork_resources/GJ_ED3/3_segmentation", bitmaps, 2);
           // dict = Magic2D.SegmentToPatch.LoadPatches(@"C:\Users\furaga\Documents\Research\Patchwork\gj_kirara2", "3_segmentation", bitmaps, 4);
                        dict = Magic2D.SegmentToPatch.LoadPatches("./settings", "3_segmentation", bitmaps, 4);

            System.Diagnostics.Debug.Assert(dict.Count == bitmaps.Count);


            // テクスチャをアセットに登録
            foreach (var kv in bitmaps)
            {
                var tex = SharpDXHelper.BitmapToTexture(kv.Value);
                resources.Add(kv.Key, tex);
                tex2bmp[tex] = kv.Value;
            }
            // パッチをキューに入れる
            for (int i = 0; i < dict.Count; i++)
            {
                string resourceKey = bitmaps.Keys.ElementAt(i);
                string textureKey = dict.Keys.ElementAt(i);
                PatchSkeletalMesh patch = dict.Values.ElementAt(i);
                renderQueryPool[resourceKey] = new RenderQuery(patch, new PatchTree(resourceKey), new List<string>() { textureKey });
            }


            // パッチをリストに表示する
            foreach (var kv in bitmaps)
            {
                var patch = dict[kv.Key.Split(':')[1]];
                var skl = patch.CopySkeleton();
                var bmp = kv.Value;
                var bmp2 = DrawSkeltonOnBmp(skl, bmp);
                patchImageList.Images.Add(kv.Key, bmp2);
                patchView.Items.Add(kv.Key, kv.Key, kv.Key);
            }

            refSkeleton = PatchSkeleton.Load("./settings/refSkeleton.skl");

            canvas.MouseWheel += canvas_MouseWheel;
        }

        Bitmap DrawSkeltonOnBmp(PatchSkeleton skl, Bitmap bmp)
        {
            Pen pen = new Pen(Brushes.Blue, 3);
            var bmp2 = new Bitmap(bmp);
            using (var g = Graphics.FromImage(bmp2))
            {
                foreach (var b in skl.bones)
                    g.DrawLine(pen, b.src.position, b.dst.position);
            }
            return bmp2;
        }

        private void timer_Tick(object sender, EventArgs e)
        {
            DrawCanvas();
        }

        void DrawCanvas()
        {
            if (renderer == null)
                return;

            // renderer.rotateCamera = true;
            renderer.BeginDraw();

            bool canConnect = PatchConnector.CanConnect(selectingQueries.Select(q => q.patch).ToList(), refSkeleton);

            foreach (var query in renderQueries)
            {
                DXColor color = DXColor.White;
                if (selectingQueries.Contains(query)) // 選択中は赤くする
                {
                    if (canConnect)
                        color = DXColor.Green;
                    else
                        color = DXColor.Red;
                }

                if (CBoxDrawMesh.Checked)
                {
                    foreach (var key in query.patchKeys)
                        renderer.DrawMesh(query.patch.Mesh, key, resources, color, canvas.ClientSize, cameraPosition);
                }

                if (CBoxDrawPolygon.Checked)
                {
                    foreach (var key in query.patchKeys)
                        renderer.DrawWireframe(query.patch.Mesh, key, color, canvas.ClientSize, cameraPosition);
                }

                renderer.ClearDepthStencilView();
            }

            // 必ずメッシュの上に描画されるように、深度バッファをクリアする
            renderer.ClearDepthStencilView();

            if (CBoxDrawRefSkeleton.Checked)
            {
                foreach (var b in refSkeleton.bones)
                    renderer.DrawLine(b.src.position, b.dst.position, 4, DXColor.Blue, canvas.ClientSize, cameraPosition);

                foreach (var j in refSkeleton.joints)
                    renderer.DrawPoint(j.position, 8, DXColor.Yellow, canvas.ClientSize, cameraPosition);
            }

            renderer.ClearDepthStencilView();

            foreach (var query in selectingQueries)
            {
                if (CBoxDrawSkeleton.Checked)
                {
                    foreach (var b in query.patch.CopySkeleton().bones)
                        renderer.DrawLine(b.src.position, b.dst.position, 6, DXColor.Orange, canvas.ClientSize, cameraPosition);
                }
            }

            renderer.EndDraw();
        }

        // この中で描画するとバッファリングのせい？で画面がちらつく
        private void canvas_Paint(object sender, PaintEventArgs e)
        {
        }

        //--------------------------------------------------------------------
        // canvas内でのマウス操作
        //--------------------------------------------------------------------

        PointF prevMousePos = new PointF();
        PatchSkeletonJoint draggedJoint = null;

        private void canvas_MouseDown(object sender, MouseEventArgs e)
        {
            // マウスホイールができるようにフォーカスを当てる
            canvas.Focus();

            // 関節の移動
            draggedJoint = null;
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
           //     renderer.ToBitmap();


                Vector3 p = renderer.Unproject(e.Location, 0, canvas.ClientSize, cameraPosition);
                draggedJoint = refSkeleton.GetNearestJoint(new PointF(p.X, p.Y), 30, new System.Drawing.Drawing2D.Matrix());
            }

            // パッチの選択
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                if (draggedJoint == null)
                {
                    // 関節以外の部分がクリックされたらパッチの選択をチェックする
                    Vector3 p = renderer.Unproject(e.Location, 0, canvas.ClientSize, cameraPosition);
                    bool anyHit = false;
                    foreach (var q in renderQueries)
                    {
                        // このパッチ（クエリ）がクリックされたら選択状態をトグルする
                        if (PatchMeshCollision.IsHit(q.patch.Mesh, new PointF(p.X, p.Y)))
                        {
                            anyHit = true;
                            if (selectingQueries.Contains(q))
                                selectingQueries.Remove(q);
                            else
                                selectingQueries.Add(q);

                            //
                            UpdateConnectablePatchView(renderQueryPool, selectingQueries, refSkeleton);
                        }
                    }
                    if (!anyHit)
                    {
                        // なにもないところをクリックしたら選択解除
                        selectingQueries.Clear();

                        //
                        UpdateConnectablePatchView(renderQueryPool, selectingQueries, refSkeleton);
                    }
                }
            }

            // マウスドラッグ用にクリック位置を保存
            prevMousePos = e.Location;
        }

        private void canvas_MouseUp(object sender, MouseEventArgs e)
        {
            prevMousePos = PointF.Empty;

            draggedJoint = null;
        }

        private void canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
            {
                FTimer.AllReset();

                if (draggedJoint != null)
                {
                    Vector3 p = renderer.Unproject(e.Location, 0, canvas.ClientSize, cameraPosition);
                    PatchSkeletonFKMover.MoveJoint(refSkeleton, draggedJoint, new PointF(p.X, p.Y));
                    foreach (var query in renderQueries)
                    {
                        PatchSkeletonFitting.Fitting(query.patch, refSkeleton);
                    }
                }

//                Console.WriteLine(FTimer.Output());
            }


            if (e.Button == System.Windows.Forms.MouseButtons.Right)
            {
                PointF pos = e.Location;
                if (!prevMousePos.IsEmpty)
                {
                    // fix: ズーム度合いに合わせて移動量を増減させる
                    cameraPosition.X -= pos.X - prevMousePos.X;
                    cameraPosition.Y -= pos.Y - prevMousePos.Y;
                }
                prevMousePos = pos;
            }
        }

        void canvas_MouseWheel(object sender, MouseEventArgs e)
        {
            cameraPosition.Z += e.Delta * 0.3f;
        }


        //
        private void patchView_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (patchView.SelectedItems.Count >= 0)
                AddQuery(patchView.SelectedItems[0].ImageKey);
        }

        private void patchView_ItemDrag(object sender, ItemDragEventArgs e)
        {
            patchView.DoDragDrop(e.Item as ListViewItem, DragDropEffects.Move);
        }

        private void patchVieww_DragOver(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.Move;
        }

        private void canvas_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.Move;
        }

        private void canvas_DragDrop(object sender, DragEventArgs e)
        {
            // リストビューからドラッグアンドドロップされた
            if (e.Data.GetData(typeof(ListViewItem)) != null)
            {
                List<string> imageKeys = new List<string>();
                var item = e.Data.GetData(typeof(ListViewItem)) as ListViewItem;
                if (item != null)
                    AddQuery(item.ImageKey);
            }
        }

        //--------------------------------------------------------------------
        //
        //--------------------------------------------------------------------

        void AddQuery(string resourceKey)
        {
            if (renderQueryPool.ContainsKey(resourceKey))
            {
                Console.WriteLine("D&D: " + resourceKey);
                var _q = renderQueryPool[resourceKey];
                var q = new RenderQuery(PatchSkeletalMesh.Copy(_q.patch), _q.patchTree, _q.patchKeys);
                resources.DuplicateResources(_q.patch.Mesh, q.patch.Mesh);

                // 初めて登録される場合、ARAP変形を有効にしてスケルトにフィットさせる
                if (!renderQueries.Contains(q))
                {
                    q.patch.Mesh.BeginDeformation();
                    PatchSkeletonFitting.Fitting(q.patch, refSkeleton);
                }

                if (renderQueries.Contains(q))
                    renderQueries.Remove(q);
                renderQueries.Add(q);

                // 追加したパッチを選択モードにする
                selectingQueries.Add(q);

                UpdateConnectablePatchView(renderQueryPool, selectingQueries, refSkeleton);
            }
        }

        // スケルトンとカメラ位置だけ保存・復元する
        private void saveSToolStripMenuItem_Click(object sender, EventArgs e)
        {
            saveFileDialog.RestoreDirectory = true;
            saveFileDialog.Filter = "*.xml|*.xml";
            if (saveFileDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;

            string dir = System.IO.Path.GetDirectoryName(saveFileDialog.FileName);
            string filename = System.IO.Path.GetFileNameWithoutExtension(saveFileDialog.FileName);

            var save = new SaveObjects()
            {
                cameraX = cameraPosition.X,
                cameraY = cameraPosition.Y,
                cameraZ = cameraPosition.Z,
                refSkeleton = refSkeleton
            };

            FLib.ForceSerializer.Serialize(save, dir, filename);
        }

        private void openOToolStripMenuItem_Click(object sender, EventArgs e)
        {
            openFileDialog.RestoreDirectory = true;
            openFileDialog.Filter = "*.xml|*.xml";
            if (openFileDialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                return;

            string dir = System.IO.Path.GetDirectoryName(openFileDialog.FileName);
            string filename = System.IO.Path.GetFileNameWithoutExtension(openFileDialog.FileName);

            var saved = FLib.ForceSerializer.Deserialize<SaveObjects>(dir, filename);
            cameraPosition = new Vector3(saved.cameraX, saved.cameraY, saved.cameraZ);
            refSkeleton = saved.refSkeleton;

        }

        // selectingPatchesをすべて結合する
        private void combineCToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string newKey = "combined" + combinedCount;

            HashSet<string> patchKeySet = new HashSet<string>();
            for (int i = 0; i < selectingQueries.Count; i++)
            {
                foreach (string k in selectingQueries[i].patchKeys)
                    patchKeySet.Add(k);
            }

            PatchSkeletalMesh newMesh = PatchConnector.Connect(selectingQueries.Select(q => q.patch).ToList(), refSkeleton, resources);
            
            
            PatchTree newTree = new PatchTree(null, null);

            if (newMesh == null)
                return;

            newMesh.Mesh.BeginDeformation();

            renderQueryPool.Add(newKey, new RenderQuery(newMesh, newTree, patchKeySet.ToList()));
            combinedCount++;


            foreach (var q in selectingQueries)
                renderQueries.Remove(q);
            renderQueries.Add(renderQueryPool[newKey]);

            selectingQueries.Clear();
        }

        private void deleteDToolStripMenuItem_Click(object sender, EventArgs e)
        {
            foreach (var q in selectingQueries)
            {
                renderQueries.Remove(q);
                resources.RemoveResources(q.patch.Mesh);
            }
            selectingQueries.Clear();
        }

        private void scaleSlider_Scroll(object sender, EventArgs e)
        {
            float scale = scaleSlider.Value * 0.1f;
            foreach (var q in selectingQueries)
            {
                q.patch.Scale(scale, scale);
                PatchSkeletonFitting.Fitting(q.patch, refSkeleton);
            }
        }

        private void stretchSlider_Scroll(object sender, EventArgs e)
        {
            float srcStretch = (srcStretchSlider.Value - 50) * 0.1f;
            float dstStretch = (dstStretchSlider.Value - 50) * 0.1f;
            foreach (var q in selectingQueries)
            {
                q.patch.Stretch(srcStretch, dstStretch, refSkeleton);
                PatchSkeletonFitting.Fitting(q.patch, refSkeleton);
            }
        }

        private void reverseLeftrightRToolStripMenuItem_Click(object sender, EventArgs e)
        {
            foreach (var q in selectingQueries)
            {
                q.patch.Scale(-1, 1);
                PatchSkeletonFitting.Fitting(q.patch, refSkeleton);
            }
        }

        private void reverseJointNameJToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // 選択中のパッチのsklを左右対称にする
            // 対象な関節のマッピングはsymJoints.txtに記述される
            Dictionary<string, string> map = new Dictionary<string, string>();
            string symJointPath = "settings/symJoints.txt";
            foreach (var line in System.IO.File.ReadAllLines(symJointPath))
            {
                var tokens = line.Split(',');
                if (tokens.Length != 2)
                    continue;
                map[tokens[0].Trim()] = tokens[1].Trim();
                map[tokens[1].Trim()] = tokens[0].Trim();
            }

            foreach (var q in selectingQueries)
            {
                q.patch.MapJointNames(map);
                PatchSkeletonFitting.Fitting(q.patch, refSkeleton);
                PatchSkeletonFitting.Fitting(q.patch, refSkeleton);
            }
        }

        //---------------------------------------------------------------------------

        private void bringToFrontToolStripMenuItem_Click(object sender, EventArgs e)
        {
            for (int i = renderQueries.Count - 2; i >= 0; i--)
            {
                if (selectingQueries.Contains(renderQueries[i]))
                {
                    var _q = renderQueries[i + 1];
                    renderQueries[i + 1] = renderQueries[i];
                    renderQueries[i] = _q;
                }
            }

        }

        private void bringForwardToolStripMenuItem_Click(object sender, EventArgs e)
        {
            renderQueries.RemoveAll(q => selectingQueries.Contains(q));
            renderQueries.AddRange(selectingQueries.Where(q => !renderQueries.Contains(q)).ToList());
        }

        private void bringToBackToolStripMenuItem_Click(object sender, EventArgs e)
        {
            for (int i = 1; i < renderQueries.Count; i++)
            {
                if (selectingQueries.Contains(renderQueries[i]))
                {
                    var _q = renderQueries[i - 1];
                    renderQueries[i - 1] = renderQueries[i];
                    renderQueries[i] = _q;
                }
            }
        }

        private void bringBackwardToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var _qs = renderQueries.ToList();
            renderQueries.Clear();
            renderQueries.AddRange(selectingQueries);
            renderQueries.AddRange(_qs.Where(q => !renderQueries.Contains(q)).ToList());
        }


        //--------------------------------------------------------------------
        // 選択中のパッチと接続可能なパッチの列挙
        //--------------------------------------------------------------------

        List<string> ConnectableRenderQueries(Dictionary<string, RenderQuery> renderQueryPool, List<RenderQuery> queries, PatchSkeleton refSkeleton)
        {
            List<PatchSkeletalMesh> smeshes = new List<PatchSkeletalMesh>();
            foreach (var q in queries)
                smeshes.Add(q.patch);

            var cmeshes = new List<string>();
            foreach (var kv in renderQueryPool)
            {
                smeshes.Add(kv.Value.patch);
                if (PatchConnector.CanConnect(smeshes, refSkeleton))
                    cmeshes.Add(kv.Key);
                smeshes.RemoveAt(smeshes.Count - 1);
            }

//            cmeshes.OrderBy(// TODO);

            return cmeshes;
        }

        void UpdateConnectablePatchView(Dictionary<string, RenderQuery> renderQueryPool, List<RenderQuery> queries, PatchSkeleton refSkeleton)
        {
            var addKeys = ConnectableRenderQueries(renderQueryPool, selectingQueries, refSkeleton);
            var removeItems = new List<ListViewItem>();
            foreach (ListViewItem item in connectablePatchView.Items)
            {
                if (!addKeys.Contains(item.ImageKey))
                    removeItems.Add(item);
            }

            foreach (var item in removeItems)
                connectablePatchView.Items.Remove(item);

            foreach (var k in addKeys)
                connectablePatchView.Items.Add(k, k);
        }

        private void connectablePatchView_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (connectablePatchView.SelectedItems.Count >= 0)
                AddQuery(connectablePatchView.SelectedItems[0].ImageKey);
        }

        /// <summary>
        /// 現在のrefSkeletonの形に最もフィットするpatch群を探索してcanvas上に自動挿入
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void placePatchesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // boneKey -> it's cost
            var matchCostDict = new Dictionary<string, float>();

            // bone -> render queries that have the bone
            var bone2queryKvs = new Dictionary<string, HashSet<KeyValuePair<string, RenderQuery>>>();
            foreach (var b in refSkeleton.bones)
                bone2queryKvs[b.src.name + "->" + b.dst.name] = new HashSet<KeyValuePair<string, RenderQuery>>();

            var query2skl = new Dictionary<RenderQuery, PatchSkeleton>();

            foreach (var kv in renderQueryPool)
            {
                var skl = kv.Value.patch.CopySkeleton();
                query2skl[kv.Value] = skl;

                // set matchCostDict
                matchCostDict[kv.Key] = MatchCost(skl, refSkeleton);

                // set bone2querKvs
                foreach (var b in skl.bones)
                {
                    var key = b.src.name + "->" + b.dst.name;
                    if (bone2queryKvs.ContainsKey(key))
                        bone2queryKvs[key].Add(kv);
                }
            }


            // patchが割り当てられていないboneのリスト
            var uncheckedBones = refSkeleton.bones.ToList();

            // 追加するpatch (RenderQuery) のリスト
            List<RenderQuery> queries = new List<RenderQuery>();

            while (uncheckedBones.Count > 0)
            {
                int orgCnt = uncheckedBones.Count;

                var ub = uncheckedBones[0];

                // ボーンubに代入しうるpatchのリスト（マッチ率順でソート済み）
                var queryKvs = bone2queryKvs[bone2key(ub)].OrderByDescending(kv => matchCostDict[kv.Key]);
               
                KeyValuePair<string, RenderQuery> appendKv = new KeyValuePair<string,RenderQuery>();

                foreach (var kv in queryKvs)
                {
                    // 割り当てられていないボーンだけからなるpatchのみ採用
                    if (query2skl[kv.Value].bones.All(b => uncheckedBones.Any(_b => bone2key(b) == bone2key(_b)))) 
                    {
                        appendKv = kv;
                        break;
                    }
                }

                // appendKvを追加し、さらにそれに接続可能なpatchを再帰的に探してひとつずつ追加
                if (appendKv.Key != null)
                {
                    AddQuery(appendKv.Key);
                    queries.Add(appendKv.Value);
                    while (true)
                    {
                        var keys = ConnectableRenderQueries(renderQueryPool, queries, refSkeleton);
                        if (keys.Count <= 0)
                            break;
                        var sortedKeys = keys.OrderByDescending(k => matchCostDict[k]).ToList();
                        AddQuery(sortedKeys[0]);
                        queries.Add(renderQueryPool[sortedKeys[0]]);
                    }
                }

                foreach (var _q in queries)
                {
                    var skl = query2skl[_q];
                    foreach (var _b in skl.bones)
                    {
                        if (uncheckedBones.Any(__b => bone2key(__b) == bone2key(_b)))
                        {
                            var removeBone = uncheckedBones.First(__b => bone2key(__b) == bone2key(_b));
                            uncheckedBones.Remove(removeBone);
                        }
                    }
                }

                if (orgCnt == uncheckedBones.Count)
                    break;
            }
            
        }

        string bone2key(PatchSkeletonBone bone)
        {
            return bone.src.name + "->" + bone.dst.name;
        }

        float MatchCost(PatchSkeleton skl, PatchSkeleton refSkeleton)
        {
            Dictionary<string, List<PatchSkeletonBone>> bonePairs = new Dictionary<string,List<PatchSkeletonBone>>();
            foreach (var b in skl.bones)
            {
                var key = bone2key(b);
                bonePairs[key] = new List<PatchSkeletonBone>() { b };
            }
            foreach (var b in refSkeleton.bones)
            {
                var key = bone2key(b); ;
                if (bonePairs.ContainsKey(key))
                    bonePairs[key].Add(b);
            }

            if (bonePairs.Count <= 0)
                return float.MaxValue;

            float cost = 0;

            foreach (var kv in bonePairs)
            {
                if (kv.Value.Count != 2)
                    continue;
                
                var b1 = kv.Value[0];
                var b2 = kv.Value[1];

                float vx1 = b1.dst.position.X - b1.src.position.X;
                float vy1 = b1.dst.position.Y - b1.src.position.Y;
                float sqLen1 = vx1 * vx1 + vy1 * vy1; 
                float vx2 = b2.dst.position.X - b2.src.position.X;
                float vy2 = b2.dst.position.Y - b2.src.position.Y;
                float sqLen2 = vx2 * vx2 + vy2 * vy2;

                float dx = vx1 - vx2;
                float dy = vy1 - vy2;
                
                cost = (dx * dx + dy * dy) / Math.Max(sqLen1, sqLen2);
            }

            cost /= bonePairs.Count;

            return cost;
        }

    }

}