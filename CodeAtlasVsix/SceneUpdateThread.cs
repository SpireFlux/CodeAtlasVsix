﻿using Microsoft.Msagl.Drawing;
using Microsoft.Msagl.Layout.Layered;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace CodeAtlasVSIX
{
    class SceneUpdateThread
    {
        class ItemData
        {

        }
        int m_sleepTime = 30;
        Thread m_thread = null;
        bool m_isActive = true;
        int m_selectTimeStamp = 0;
        Dictionary<string, ItemData> m_itemSet = new Dictionary<string, ItemData>();
        int m_edgeNum = 0;

        public SceneUpdateThread(CodeScene scene)
        {
            m_thread = new Thread(new ThreadStart(Run));
        }

        public void Start()
        {
            m_thread.Start();
        }

        void Run()
        {
            var scene = UIManager.Instance().GetScene();
            while(true)
            {
                if(m_isActive)
                {
                    scene.AcquireLock();
                    var itemDict = scene.GetItemDict();
                    if(scene.m_isLayoutDirty)
                    {
                        UpdateLayeredLayoutWithComp();

                        // update internal dict
                        m_itemSet.Clear();
                        foreach(var item in itemDict)
                        {
                            m_itemSet.Add(item.Key, new ItemData());
                        }
                        scene.m_isLayoutDirty = false;
                    }

                    MoveItems();
                    UpdateCallOrder();
                    if (m_selectTimeStamp != scene.m_selectTimeStamp)
                    {
                        scene.UpdateCurrentValidScheme();
                        scene.UpdateCandidateEdge();
                        UpdateLegend();
                        m_selectTimeStamp = scene.m_selectTimeStamp;
                    }
                    scene.ReleaseLock();
                    //InvalidateScene();
                    // System.Console.Write("running\n");
                }
                Thread.Sleep(m_sleepTime);
            }
        }

        void UpdateLayeredLayoutWithComp()
        {
            var scene = UIManager.Instance().GetScene();

            Graph graph = new Graph();

            var itemDict = scene.GetItemDict();
            var edgeDict = scene.GetEdgeDict();
            foreach(var item in itemDict)
            {
                var node = graph.AddNode(item.Key);
            }
            foreach(var edge in edgeDict)
            {
                var key = edge.Key;
                graph.AddEdge(key.Item1, key.Item2);
            }
            graph.Attr.LayerDirection = LayerDirection.LR;
            graph.CreateGeometryGraph();
            var layerSetting = graph.LayoutAlgorithmSettings as SugiyamaLayoutSettings;
            if (layerSetting != null)
            {
                layerSetting.LayerSeparation = 80;
            }
            foreach (var msaglNode in graph.GeometryGraph.Nodes)
            {
                var node = (Microsoft.Msagl.Drawing.Node)msaglNode.UserData;
                var sceneNode = itemDict[node.Id];
                double radius = sceneNode.GetRadius();
                double width = sceneNode.GetWidth();
                double height = sceneNode.GetHeight();
                msaglNode.BoundaryCurve = NodeBoundaryCurves.GetNodeBoundaryCurve(node, width, height);
            }
            Microsoft.Msagl.Miscellaneous.LayoutHelpers.CalculateLayout(graph.GeometryGraph, graph.LayoutAlgorithmSettings, new Microsoft.Msagl.Core.CancelToken());

            foreach (var msaglNode in graph.GeometryGraph.Nodes)
            {
                var node = (Microsoft.Msagl.Drawing.Node)msaglNode.UserData;
                var nodeBegin = node.Pos.Y - node.Height * 0.5;
                var sceneNode = itemDict[node.Id];
                double radius = sceneNode.GetRadius();
                double width = sceneNode.GetWidth();
                double height = sceneNode.GetHeight();
                var pos = node.Pos;
                sceneNode.SetTargetPos(new Point(pos.X, nodeBegin + radius));
            }
        }
        
        void UpdateLegend()
        {
            var scene = UIManager.Instance().GetScene();
            scene.View.InvalidateLegend();
            scene.View.InvalidateScheme();
        }

        void MoveItems()
        {
            var scene = UIManager.Instance().GetScene();
            scene.MoveItems();
            if(scene.View != null)
            {
                Point centerPnt;
                bool res = scene.GetSelectedCenter(out centerPnt);
                if (res && scene.IsAutoFocus())
                {
                    scene.View.MoveView(centerPnt);
                }
            }
        }

        void UpdateCallOrder()
        {
            var scene = UIManager.Instance().GetScene();
            var edgeDict = scene.GetEdgeDict();
            var itemDict = scene.GetItemDict();
            foreach (var item in edgeDict)
            {
                var key = item.Key;
                var edge = item.Value;
                edge.m_orderData = null;
                edge.m_isConnectedToFocusNode = false;
            }

            foreach (var item in itemDict)
            {
                item.Value.m_isConnectedToFocusNode = false;
            }

            var items = scene.SelectedItems();
            if (items.Count == 0)
            {
                return;
            }

            var selectedItem = items[0];
            UpdateCallOrderByItem(selectedItem);

            var selectedUIItem = selectedItem as CodeUIItem;
            if (selectedUIItem != null && selectedUIItem.IsFunction())
            {
                var caller = new List<CodeUIItem>();
                foreach (var item in edgeDict)
                {
                    var key = item.Key;
                    var edge = item.Value;
                    var srcItem = itemDict[key.Item1];
                    if (key.Item2 == selectedUIItem.GetUniqueName() && srcItem.IsFunction())
                    {
                        caller.Add(srcItem);
                    }
                }
                if (caller.Count == 1)
                {
                    UpdateCallOrderByItem(caller[0]);
                }
            }
        }

        void UpdateCallOrderByItem(System.Windows.Shapes.Shape item)
        {
            var scene = UIManager.Instance().GetScene();
            var itemDict = scene.GetItemDict();
            var isEdgeSelected = false;
            var edgeItem = item as CodeUIEdgeItem;
            if (edgeItem != null)
            {
                isEdgeSelected = true;
                if (itemDict.ContainsKey(edgeItem.m_srcUniqueName) &&
                    itemDict.ContainsKey(edgeItem.m_tarUniqueName))
                {
                    var srcItem = itemDict[edgeItem.m_srcUniqueName];
                    var dstItem = itemDict[edgeItem.m_tarUniqueName];
                    srcItem.m_isConnectedToFocusNode = true;
                    dstItem.m_isConnectedToFocusNode = true;
                    item = srcItem;
                }
            }

            var nodeItem = item as CodeUIItem;
            if (nodeItem == null || !nodeItem.IsFunction())
            {
                return;
            }

            var edgeList = new List<CodeUIEdgeItem>();
            double minXRange = double.MaxValue;
            double maxXRange = double.MinValue;
            var itemUniqueName = nodeItem.GetUniqueName();
            var edgeDict = scene.GetEdgeDict();
            foreach (var edgePair in edgeDict)
            {
                var key = edgePair.Key;
                var edge = edgePair.Value;
                var srcItem = itemDict[key.Item1];
                var tarItem = itemDict[key.Item2];
                if (key.Item1 == itemUniqueName && tarItem.IsFunction())
                {
                    edgeList.Add(edge);
                    Point srcPos, tarPos;
                    edge.GetNodePos(out srcPos, out tarPos);
                    minXRange = Math.Min(tarPos.X, minXRange);
                    maxXRange = Math.Max(tarPos.X, maxXRange);
                }
                edge.m_isConnectedToFocusNode = key.Item1 == itemUniqueName || key.Item2 == itemUniqueName;

                if (isEdgeSelected == false)
                {
                    if (key.Item1 == itemUniqueName && tarItem.IsFunction())
                    {
                        tarItem.m_isConnectedToFocusNode = true;
                    }
                    if (key.Item2 == itemUniqueName && srcItem.IsFunction())
                    {
                        srcItem.m_isConnectedToFocusNode = true;
                    }
                }
            }

            if (edgeList.Count <= 1)
            {
                return;
            }
            double basePos = 0.0;
            double itemX = nodeItem.Pos.X;
            if (minXRange < itemX && maxXRange > itemX)
            {
                basePos = double.NaN;
            }
            else if (minXRange >= itemX)
            {
                basePos = minXRange;
            }
            else if (maxXRange <= itemX)
            {
                basePos = maxXRange;
            }

            edgeList.Sort((x, y) => x.ComparePos(y));
            for (int i = 0; i < edgeList.Count; i++)
            {
                var edge = edgeList[i];
                Point srcPos, tarPos;
                edge.GetNodePos(out srcPos, out tarPos);
                double padding = srcPos.X < tarPos.X ? -8.0 : 8.0;
                double x, y;
                if (basePos == double.NaN)
                {
                    x = tarPos.X + padding;
                }
                else
                {
                    x = basePos + padding;
                }
                y = edge.FindCurveYPos(x);
                edge.m_orderData = new OrderData(i + 1, new Point(x,y));
            }
        }

        void InvalidateScene()
        {
            UIManager.Instance().GetScene().Invalidate();
        }

    }
}
