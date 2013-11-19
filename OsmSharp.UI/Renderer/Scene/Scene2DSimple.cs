﻿// OsmSharp - OpenStreetMap (OSM) SDK
// Copyright (C) 2013 Abelshausen Ben
// 
// This file is part of OsmSharp.
// 
// OsmSharp is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 2 of the License, or
// (at your option) any later version.
// 
// OsmSharp is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with OsmSharp. If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.IO;
using Ionic.Zlib;
using OsmSharp.Collections;
using OsmSharp.Collections.SpatialIndexes;
using OsmSharp.Collections.SpatialIndexes.Serialization.v2;
using OsmSharp.Math.Primitives;
using OsmSharp.UI.Renderer.Scene.Scene2DPrimitives;
using ProtoBuf;
using ProtoBuf.Meta;

namespace OsmSharp.UI.Renderer.Scene
{
    /// <summary>
    /// Contains all objects that need to be rendered.
    /// </summary>
    public class Scene2DSimple : Scene2D
    {
        /// <summary>
        /// Holds the string table.
        /// </summary>
        private ObjectTable<string> _stringTable;

        /// <summary>
        /// Holds the zoom ranges.
        /// </summary>
        private ObjectTable<ZoomRanges> _zoomRanges;

        /// <summary>
        /// Holds the index of point.
        /// </summary>
        private List<KeyValuePair<double, double>> _pointIndex;

        /// <summary>
        /// Holds the point styles.
        /// </summary>
        private ObjectTable<StylePoint> _pointStyles;

        /// <summary>
        /// Holds the text styles.
        /// </summary>
        private ObjectTable<StyleText> _textStyles;

        /// <summary>
        /// Holds the line styles.
        /// </summary>
        private ObjectTable<StyleLine> _lineStyles;

        /// <summary>
        /// Holds the polygon styles.
        /// </summary>
        private ObjectTable<StylePolygon> _polygonStyles;

        /// <summary>
        /// Holds the scene objects per layer.
        /// </summary>
        private List<SceneObject> _sceneObjects;

        /// <summary>
        /// Holds the index of points.
        /// </summary>
        private List<KeyValuePair<double[], double[]>> _pointsIndex;

        /// <summary>
        /// Holds the index of images.
        /// </summary>
        private List<byte[]> _imageIndex;

        /// <summary>
        /// Holds the simplification zoom factor.
        /// </summary>
        private float _simplifyAt;

        /// <summary>
        /// Creates a new simple scene.
        /// </summary>
        public Scene2DSimple()
        {
            _simplifyAt = (float)new OsmSharp.Math.Geo.Projections.WebMercator().ToZoomFactor(16);

            // string table.
            _stringTable = new ObjectTable<string>(true);

            // zoom ranges.
            _zoomRanges = new ObjectTable<ZoomRanges>(true);

            // styles.
            _pointStyles = new ObjectTable<StylePoint>(true);
            _textStyles = new ObjectTable<StyleText>(true);
            _lineStyles = new ObjectTable<StyleLine>(true);
            _polygonStyles = new ObjectTable<StylePolygon>(true);

            // geo indexes.
            _pointIndex = new List<KeyValuePair<double, double>>();
            _pointsIndex = new List<KeyValuePair<double[], double[]>>();

            // scene objects.
            _sceneObjects = new List<SceneObject>();

            // lines/polygons.
            _imageIndex = new List<byte[]>();
        }

        /// <summary>
        /// Calculates the simplification epsilon.
        /// </summary>
        /// <returns>The simplification epsilon.</returns>
        /// <param name="zoomFactor">Zoom factor.</param>
        private float CalculateSimplificationEpsilon(float zoomFactor)
        {
            double pixelWidth = 1 / zoomFactor * 4;
            return (float)pixelWidth;
        }

        /// <summary>
        /// Adds the given point.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns></returns>
        public override uint AddPoint(double x, double y)
        {
            uint id = (uint)_pointIndex.Count;
            _pointIndex.Add(new KeyValuePair<double, double>(x, y));
            return id;
        }

        /// <summary>
        /// Adds the given points.
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <returns>The id of the given points-series or null when the geometry is not visible in this scene.</returns>
        public override uint? AddPoints(double[] x, double[] y)
        {
            uint id = (uint)_pointsIndex.Count;

            // simplify the algorithm.
            double epsilon = this.CalculateSimplificationEpsilon(_simplifyAt);
            double[][] simplified = OsmSharp.Math.Algorithms.SimplifyCurve.Simplify(new double[][] { x, y },
                                                            epsilon);
            BoxF2D rectangle = new BoxF2D(x, y);
            if (rectangle.Delta[0] < epsilon && rectangle.Delta[1] < epsilon)
            {
                return null;
            }
            double distance = epsilon * 2;
            if (simplified[0].Length == 2)
            { // check if the simplified version is smaller than epsilon.
                OsmSharp.Math.Primitives.PointF2D point1 = new OsmSharp.Math.Primitives.PointF2D(
                    simplified[0][0], simplified[0][1]);
                OsmSharp.Math.Primitives.PointF2D point2 = new OsmSharp.Math.Primitives.PointF2D(
                    simplified[1][0], simplified[0][1]);
                distance = point1.Distance(point2);
            }
            if (distance >= epsilon)
            {
                _pointsIndex.Add(new KeyValuePair<double[], double[]>(simplified[0], simplified[1]));

                return id;
            }
            return null;
        }

        /// <summary>
        /// Adds the given image.
        /// </summary>
        /// <param name="data"></param>
        /// <returns></returns>
        public override uint AddImage(byte[] data)
        {
            uint id = (uint)_imageIndex.Count;
            _imageIndex.Add(data);
            return id;
        }

        /// <summary>
        /// Build the runtime type model.
        /// </summary>
        /// <returns></returns>
        private static RuntimeTypeModel BuildRuntimeTypeModel()
        {
            // build protobuf runtime type model.
            RuntimeTypeModel typeModel = RuntimeTypeModel.Create();
            typeModel.Add(typeof(StylePoint), true);
            typeModel.Add(typeof(StyleLine), true);
            typeModel.Add(typeof(StylePolygon), true);
            typeModel.Add(typeof(StyleText), true);
            typeModel.Add(typeof(ZoomRanges), true);
            typeModel.Add(typeof(SceneIndex), true);
            return typeModel;
        }

        /// <summary>
        /// Serializes the given scene.
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="compress"></param>
        public override void Serialize(Stream stream, bool compress)
        {
            RuntimeTypeModel typeModel = Scene2DSimple.BuildRuntimeTypeModel();

            // index index.
            SceneIndex sceneIndex = new SceneIndex();
            sceneIndex.LineStyles = _lineStyles.ToArray();
            sceneIndex.PointStyles = _pointStyles.ToArray();
            sceneIndex.PolygonStyles = _polygonStyles.ToArray();
            sceneIndex.TextStyles = _textStyles.ToArray();
            sceneIndex.ZoomRanges = _zoomRanges.ToArray();

            // serialize and write.
            stream.Seek(4, SeekOrigin.Begin);
            long indexStart = stream.Position;
            typeModel.Serialize(stream, sceneIndex);

            // write size.
            long indexSize = stream.Position - indexStart;
            stream.Seek(0, SeekOrigin.Begin);
            stream.Write(BitConverter.GetBytes(indexSize), 0, 4);
            stream.Seek(4 + indexSize, SeekOrigin.Begin);

            // index into r-tree.
            RTreeMemoryIndex<int> memoryIndex = new RTreeMemoryIndex<int>(100, 250);
            for(int id = 0; id < _sceneObjects.Count; id++)
            { // loop over all primitives in order.
                SceneObject sceneObject = _sceneObjects[id];
                switch (sceneObject.Enum)
                {
                    case SceneObjectType.IconObject:
                    case SceneObjectType.PointObject:
                    case SceneObjectType.TextObject:
                        KeyValuePair<double, double> geo = _pointIndex[(int)sceneObject.GeoId];
                        PointF2D point = new PointF2D(geo.Key, geo.Value);
                        memoryIndex.Add(new BoxF2D(point), id);
                        break;
                    case SceneObjectType.LineObject:
                    case SceneObjectType.LineTextObject:
                    case SceneObjectType.PolygonObject:
                        KeyValuePair<double[], double[]> geos = _pointsIndex[(int)sceneObject.GeoId];
                        memoryIndex.Add(new BoxF2D(geos.Key, geos.Value), id);
                        break;
                }
            }

            // serialize the r-tree.
            RTreeSerializer serializer = new RTreeSerializer(this, compress);
            serializer.Serialize(stream, memoryIndex);
        }

        /// <summary>
        /// Deserializes the given stream into scene.
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="isCompressed"></param>
        /// <returns></returns>
        public static IScene2DPrimitivesSource Deserialize(Stream stream, bool isCompressed)
        {
            //RuntimeTypeModel typeModel = Scene2DSimple.BuildRuntimeTypeModel();
            
            //// get the index data.
            //byte[] indexLengthBytes = new byte[4];
            //stream.Read(indexLengthBytes, 0, 4);
            //int indexLength = BitConverter.ToInt32(indexLengthBytes, 0);
            //byte[] indexBytes = new byte[indexLength];
            //stream.Read(indexBytes, 0, indexLength);
            //MemoryStream indexStream = new MemoryStream(indexBytes);
            //SceneIndex sceneIndex = typeModel.Deserialize(indexStream, null, typeof(SceneIndex)) as SceneIndex;

            //// serialize the r-tree.
            //RTreeSerializer serializer = new RTreeSerializer(sceneIndex, isCompressed);
            //ISpatialIndexReadonly<int> index = serializer.Deserialize(stream, true);

            //return new Scene2DPrimitivesSource(index, sceneIndex);
            throw new NotImplementedException();
        }

        /// <summary>
        /// Clears all data from this scene.
        /// </summary>
        public override void Clear()
        {            
            // string table.
            _stringTable.Clear();

            // zoom ranges.
            _zoomRanges.Clear();

            // styles.
            _pointStyles.Clear();
            _textStyles.Clear();
            _lineStyles.Clear();
            _polygonStyles.Clear();

            // geo indexes.
            _pointIndex.Clear();
            _pointsIndex.Clear();

            // scene objects.
            _sceneObjects.Clear();

            // lines/polygons.
            _imageIndex.Clear();
        }

        /// <summary>
        /// Returns the number of objects in this scene.
        /// </summary>
        public override int Count
        {
            get
            {
                return _sceneObjects.Count;
            }
        }

        /// <summary>
        /// Returns the object with the given id.
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public override List<IScene2DPrimitive> Get(uint id)
        {
            List<IScene2DPrimitive> primitives = new List<IScene2DPrimitive>(); 
            SceneObject sceneObject = _sceneObjects[(int)id];

            KeyValuePair<double, double> point;
            KeyValuePair<double[], double[]> points;
            switch (sceneObject.Enum)
            {
                case SceneObjectType.IconObject:
                    IconObject icon = sceneObject as IconObject;
                    point = _pointIndex[(int)icon.GeoId];
                    primitives.Add(this.ConvertToPrimitive((uint)id, icon).Primitive);
                    break;
                case SceneObjectType.LineObject:
                    LineObject line = sceneObject as LineObject;
                    points = _pointsIndex[(int)line.GeoId];
                    primitives.Add(this.ConvertToPrimitive((uint)id, line).Primitive);
                    break;
                case SceneObjectType.LineTextObject:
                    LineTextObject lineText = sceneObject as LineTextObject;
                    points = _pointsIndex[(int)lineText.GeoId];
                    primitives.Add(this.ConvertToPrimitive((uint)id, lineText).Primitive);
                    break;
                case SceneObjectType.PointObject:
                    PointObject pointObject = sceneObject as PointObject;
                    point = _pointIndex[(int)pointObject.GeoId];
                    primitives.Add(this.ConvertToPrimitive((uint)id, pointObject).Primitive);
                    break;
                case SceneObjectType.PolygonObject:
                    PolygonObject polygonObject = sceneObject as PolygonObject;
                    points = _pointsIndex[(int)polygonObject.GeoId];
                    primitives.Add(this.ConvertToPrimitive((uint)id, polygonObject).Primitive);
                    break;
                case SceneObjectType.TextObject:
                    TextObject textObject = sceneObject as TextObject;
                    point = _pointIndex[(int)textObject.GeoId];
                    primitives.Add(this.ConvertToPrimitive((uint)id, textObject).Primitive);
                    break;
            }
            return primitives;
        }

        /// <summary>
        /// Returns the objects in this scene inside the given view.
        /// </summary>
        /// <param name="view"></param>
        /// <param name="zoom"></param>
        /// <returns></returns>
        public override IEnumerable<Scene2DPrimitive> Get(View2D view, float zoom)
        {
            SortedSet<Scene2DPrimitive> primitives = new SortedSet<Scene2DPrimitive>(
                LayerComparer.GetInstance());

            for(int id = 0; id < _sceneObjects.Count; id++)
            { // loop over all primitives in order.
                SceneObject sceneObject = _sceneObjects[id];

                KeyValuePair<double, double> point;
                KeyValuePair<double[], double[]> points;
                switch (sceneObject.Enum)
                {
                    case SceneObjectType.IconObject:
                        IconObject icon = sceneObject as IconObject;
                        if (_zoomRanges.Get(icon.ZoomRangeId).Contains(zoom))
                        {
                            point = _pointIndex[(int)icon.GeoId];
                            if (view.Contains(point.Key, point.Value))
                            {
                                primitives.Add(this.ConvertToPrimitive((uint)id, icon));
                            }
                        }
                        break;
                    case SceneObjectType.LineObject:
                        LineObject line = sceneObject as LineObject;
                        points = _pointsIndex[(int)line.GeoId];
                        if (_zoomRanges.Get(line.ZoomRangeId).Contains(zoom))
                        {
                            if (view.IsVisible(points.Key, points.Value, false))
                            {
                                primitives.Add(this.ConvertToPrimitive((uint)id, line));
                            }
                        }
                        break;
                    case SceneObjectType.LineTextObject:
                        LineTextObject lineText = sceneObject as LineTextObject;
                        points = _pointsIndex[(int)lineText.GeoId];
                        if (_zoomRanges.Get(lineText.ZoomRangeId).Contains(zoom))
                        {
                            if (view.IsVisible(points.Key, points.Value, false))
                            {
                                primitives.Add(this.ConvertToPrimitive((uint)id, lineText));
                            }
                        }
                        break;
                    case SceneObjectType.PointObject:
                        PointObject pointObject = sceneObject as PointObject;
                        point = _pointIndex[(int)pointObject.GeoId];
                        if (_zoomRanges.Get(pointObject.ZoomRangeId).Contains(zoom))
                        {
                            if (view.Contains(point.Key, point.Value))
                            {
                                primitives.Add(this.ConvertToPrimitive((uint)id, pointObject));
                            }
                        }
                        break;
                    case SceneObjectType.PolygonObject:
                        PolygonObject polygonObject = sceneObject as PolygonObject;
                        points = _pointsIndex[(int)polygonObject.GeoId];
                        if (_zoomRanges.Get(polygonObject.ZoomRangeId).Contains(zoom))
                        {
                            if (view.IsVisible(points.Key, points.Value, false))
                            {
                                primitives.Add(this.ConvertToPrimitive((uint)id, polygonObject));
                            }
                        }
                        break;
                    case SceneObjectType.TextObject:
                        TextObject textObject = sceneObject as TextObject;
                        point = _pointIndex[(int)textObject.GeoId];
                        if (_zoomRanges.Get(textObject.ZoomRangeId).Contains(zoom))
                        {
                            if (view.Contains(point.Key, point.Value))
                            {
                                primitives.Add(this.ConvertToPrimitive((uint)id, textObject));
                            }
                        }
                        break;
                }
            }

            return primitives;
        }

        private class LayerComparer : IComparer<Scene2DPrimitive>
        {
            private static LayerComparer _instance = null;

            public static LayerComparer GetInstance()
            {
                if (_instance == null)
                {
                    _instance = new LayerComparer();
                }
                return _instance;
            }

            public int Compare(Scene2DPrimitive x, Scene2DPrimitive y)
            {
                if (x.Layer == y.Layer)
                { // objects with same layer, assume different.
                    return -1;
                }
                return x.Layer.CompareTo(y.Layer);
            }
        }

        #region Styles

        /// <summary>
        /// Adds the given style to the given point.
        /// </summary>
        /// <param name="pointId"></param>
        /// <param name="layer"></param>
        /// <param name="minZoom"></param>
        /// <param name="maxZoom"></param>
        /// <param name="color"></param>
        /// <param name="size"></param>
        public override uint AddStylePoint(uint pointId, uint layer, float minZoom, float maxZoom, int color, float size)
        {
            // build the zoom range.
            ZoomRanges zoomRange = new ZoomRanges()
            {
                MinZoom = minZoom,
                MaxZoom = maxZoom
            };
            uint zoomRangeId = _zoomRanges.Add(zoomRange);

            // build the style.
            StylePoint style = new StylePoint()
            {
                Color = color,
                Size = size
            };
            uint styleId = _pointStyles.Add(style);

            // add the scene object.
            uint id = (uint)_sceneObjects.Count;
            _sceneObjects.Add(new PointObject() { StyleId = styleId, Layer = layer, GeoId = pointId, ZoomRangeId = zoomRangeId });
            return id;
        }

        /// <summary>
        /// Converts the given object to a Scene2DPrimitive.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="sceneObject"></param>
        /// <returns></returns>
        private Scene2DPrimitive ConvertToPrimitive(uint id, PointObject sceneObject)
        {
            Point2D point = new Point2D();
            point.Id = id;

            // convert zoom range.
            ZoomRanges zoomRange = _zoomRanges.Get(sceneObject.ZoomRangeId);
            point.MaxZoom = zoomRange.MaxZoom;
            point.MinZoom = zoomRange.MinZoom;

            // convert style.
            StylePoint style = _pointStyles.Get(sceneObject.StyleId);
            point.Color = style.Color;
            point.Size = style.Size;

            // get the geo.
            KeyValuePair<double, double> geo = _pointIndex[(int)sceneObject.GeoId];
            point.X = geo.Key;
            point.Y = geo.Value;

            return new Scene2DPrimitive() { Layer = (int)sceneObject.Layer, Primitive = point };
        }

        /// <summary>
        /// Adds a new icon at the location of the given point.
        /// </summary>
        /// <param name="pointId"></param>
        /// <param name="layer"></param>
        /// <param name="minZoom"></param>
        /// <param name="maxZoom"></param>
        /// <param name="imageId"></param>
        /// <returns></returns>
        public override uint AddIcon(uint pointId, uint layer, float minZoom, float maxZoom, uint imageId)
        {
            // build the zoom range.
            ZoomRanges zoomRange = new ZoomRanges()
            {
                MinZoom = minZoom,
                MaxZoom = maxZoom
            };
            uint zoomRangeId = _zoomRanges.Add(zoomRange);

            // add the scene object.
            uint id = (uint)_sceneObjects.Count;
            _sceneObjects.Add(new IconObject() { StyleId = imageId, Layer = layer, GeoId = pointId, ZoomRangeId = zoomRangeId });
            return id;
        }

        /// <summary>
        /// Converts the given object to a Scene2DPrimitive.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="sceneObject"></param>
        /// <returns></returns>
        private Scene2DPrimitive ConvertToPrimitive(uint id, IconObject sceneObject)
        {
            Icon2D primitive = new Icon2D();
            primitive.Id = id;

            // convert zoom range.
            ZoomRanges zoomRange = _zoomRanges.Get(sceneObject.ZoomRangeId);
            primitive.MaxZoom = zoomRange.MaxZoom;
            primitive.MinZoom = zoomRange.MinZoom;

            // convert image.
            byte[] style = _imageIndex[(int)sceneObject.StyleId];
            primitive.Image = style;

            // get the geo.
            KeyValuePair<double, double> geo = _pointIndex[(int)sceneObject.GeoId];
            primitive.X = geo.Key;
            primitive.Y = geo.Value;

            return new Scene2DPrimitive() { Layer = (int)sceneObject.Layer, Primitive = primitive };
        }

        /// <summary>
        /// Adds text at the position of the given point.
        /// </summary>
        /// <param name="pointId"></param>
        /// <param name="layer"></param>
        /// <param name="minZoom"></param>
        /// <param name="maxZoom"></param>
        /// <param name="size"></param>
        /// <param name="text"></param>
        /// <param name="color"></param>
        /// <param name="haloColor"></param>
        /// <param name="haloRadius"></param>
        /// <param name="font"></param>
        /// <returns></returns>
        public override uint AddText(uint pointId, uint layer, float minZoom, float maxZoom, float size, string text, int color, 
            int? haloColor, int? haloRadius, string font)
        {
            // add to stringtable.
            uint textId = _stringTable.Add(text);

            // build the zoom range.
            ZoomRanges zoomRange = new ZoomRanges()
            {
                MinZoom = minZoom,
                MaxZoom = maxZoom
            };
            uint zoomRangeId = _zoomRanges.Add(zoomRange);

            // build the style.
            StyleText style = new StyleText()
            {
                Color = color,
                Size = size,
                Font = font,
                HaloColor = haloColor,
                HaloRadius = haloRadius
            };
            uint styleId = _textStyles.Add(style);

            // add the scene object.
            uint id = (uint)_sceneObjects.Count;
            _sceneObjects.Add(new TextObject() { StyleId = styleId, Layer = layer, GeoId = pointId, ZoomRangeId = zoomRangeId, TextId = textId });
            return id;
        }

        /// <summary>
        /// Converts the given object to a Scene2DPrimitive.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="sceneObject"></param>
        /// <returns></returns>
        private Scene2DPrimitive ConvertToPrimitive(uint id, TextObject sceneObject)
        {
            Text2D primitive = new Text2D();
            primitive.Id = id;

            // convert zoom range.
            ZoomRanges zoomRange = _zoomRanges.Get(sceneObject.ZoomRangeId);
            primitive.MaxZoom = zoomRange.MaxZoom;
            primitive.MinZoom = zoomRange.MinZoom;

            // convert image.
            StyleText style = _textStyles.Get(sceneObject.StyleId);
            primitive.Color = style.Color;
            primitive.Font = style.Font;
            primitive.HaloColor = style.HaloColor;
            primitive.HaloRadius = style.HaloRadius;
            primitive.Size = style.Size;

            // get the text.
            primitive.Text = _stringTable.Get(sceneObject.TextId);

            // get the geo.
            KeyValuePair<double, double> geo = _pointIndex[(int)sceneObject.GeoId];
            primitive.X = geo.Key;
            primitive.Y = geo.Value;

            return new Scene2DPrimitive() { Layer = (int)sceneObject.Layer, Primitive = primitive };
        }

        /// <summary>
        /// Adds a line with the given points and style.
        /// </summary>
        /// <param name="pointsId"></param>
        /// <param name="layer"></param>
        /// <param name="minZoom"></param>
        /// <param name="maxZoom"></param>
        /// <param name="color"></param>
        /// <param name="width"></param>
        /// <param name="lineJoin"></param>
        /// <param name="dashes"></param>
        public override uint AddStyleLine(uint pointsId, uint layer, float minZoom, float maxZoom, int color, float width, LineJoin lineJoin, int[] dashes)
        {
            // build the zoom range.
            ZoomRanges zoomRange = new ZoomRanges()
            {
                MinZoom = minZoom,
                MaxZoom = maxZoom
            };
            uint zoomRangeId = _zoomRanges.Add(zoomRange);

            // build the style.
            StyleLine style = new StyleLine()
            {
                Color = color,
                Dashes = dashes,
                LineJoin = lineJoin,
                Width = width
            };
            uint styleId = _lineStyles.Add(style);

            // add the scene object.
            uint id = (uint)_sceneObjects.Count;
            _sceneObjects.Add(new LineObject() { StyleId = styleId, Layer = layer, GeoId = pointsId, ZoomRangeId = zoomRangeId });
            return id;
        }

        /// <summary>
        /// Converts the given object to a Scene2DPrimitive.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="sceneObject"></param>
        /// <returns></returns>
        private Scene2DPrimitive ConvertToPrimitive(uint id, LineObject sceneObject)
        {
            Line2D primitive = new Line2D();
            primitive.Id = id;

            // convert zoom range.
            ZoomRanges zoomRange = _zoomRanges.Get(sceneObject.ZoomRangeId);
            primitive.MaxZoom = zoomRange.MaxZoom;
            primitive.MinZoom = zoomRange.MinZoom;

            // convert image.
            StyleLine style = _lineStyles.Get(sceneObject.StyleId);
            primitive.Color = style.Color;
            primitive.LineJoin = style.LineJoin;
            primitive.Width = style.Width;
            primitive.Dashes = style.Dashes;

            // get the geo.
            KeyValuePair<double[], double[]> geo = _pointsIndex[(int)sceneObject.GeoId];
            primitive.X = geo.Key;
            primitive.Y = geo.Value;

            return new Scene2DPrimitive() { Layer = (int)sceneObject.Layer, Primitive = primitive };
        }

        /// <summary>
        /// Adds a line text with the given points and style.
        /// </summary>
        /// <param name="pointsId"></param>
        /// <param name="layer"></param>
        /// <param name="minZoom"></param>
        /// <param name="maxZoom"></param>
        /// <param name="color"></param>
        /// <param name="size"></param>
        /// <param name="text"></param>
        /// <param name="font"></param>
        /// <param name="haloColor"></param>
        /// <param name="haloRadius"></param>
        public override uint AddStyleLine(uint pointsId, uint layer, float minZoom, float maxZoom, int color, float size, string text, string font,
            int? haloColor, int? haloRadius)
        {
            // add to stringtable.
            uint textId = _stringTable.Add(text);

            // build the zoom range.
            ZoomRanges zoomRange = new ZoomRanges()
            {
                MinZoom = minZoom,
                MaxZoom = maxZoom
            };
            uint zoomRangeId = _zoomRanges.Add(zoomRange);

            // build the style.
            StyleText style = new StyleText()
            {
                Color = color,
                Size = size,
                Font = font,
                HaloColor = haloColor,
                HaloRadius = haloRadius
            };
            uint styleId = _textStyles.Add(style);

            // add the scene object.
            uint id = (uint)_sceneObjects.Count;
            _sceneObjects.Add(new LineTextObject() { StyleId = styleId, Layer = layer, GeoId = pointsId, ZoomRangeId = zoomRangeId, TextId = textId });
            return id;
        }

        /// <summary>
        /// Converts the given object to a Scene2DPrimitive.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="sceneObject"></param>
        /// <returns></returns>
        private Scene2DPrimitive ConvertToPrimitive(uint id, LineTextObject sceneObject)
        {
            LineText2D primitive = new LineText2D();
            primitive.Id = id;

            // convert zoom range.
            ZoomRanges zoomRange = _zoomRanges.Get(sceneObject.ZoomRangeId);
            primitive.MaxZoom = zoomRange.MaxZoom;
            primitive.MinZoom = zoomRange.MinZoom;

            // convert image.
            StyleText style = _textStyles.Get(sceneObject.StyleId);
            primitive.Color = style.Color;
            primitive.Font = style.Font;
            primitive.HaloColor = style.HaloColor;
            primitive.HaloRadius = style.HaloRadius;
            primitive.Size = style.Size;

            // get the text.
            primitive.Text = _stringTable.Get(sceneObject.TextId);

            // get the geo.
            KeyValuePair<double[], double[]> geo = _pointsIndex[(int)sceneObject.GeoId];
            primitive.X = geo.Key;
            primitive.Y = geo.Value;

            return new Scene2DPrimitive() { Layer = (int)sceneObject.Layer, Primitive = primitive };
        }

        /// <summary>
        /// Adds a polygon with the given points and style.
        /// </summary>
        /// <param name="pointsId"></param>
        /// <param name="layer"></param>
        /// <param name="minZoom"></param>
        /// <param name="maxZoom"></param>
        /// <param name="color"></param>
        /// <param name="width"></param>
        /// <param name="fill"></param>
        public override uint AddStylePolygon(uint pointsId, uint layer, float minZoom, float maxZoom, int color, float width, bool fill)
        {
            // build the zoom range.
            ZoomRanges zoomRange = new ZoomRanges()
            {
                MinZoom = minZoom,
                MaxZoom = maxZoom
            };
            uint zoomRangeId = _zoomRanges.Add(zoomRange);

            // build the style.
            StylePolygon style = new StylePolygon()
            {
                Color = color,
                Fill = fill,
                Width = width
            };
            uint styleId = _polygonStyles.Add(style);

            // add the scene object.
            uint id = (uint)_sceneObjects.Count;
            _sceneObjects.Add(new PolygonObject() { StyleId = styleId, Layer = layer, GeoId = pointsId, ZoomRangeId = zoomRangeId });
            return id;
        }

        /// <summary>
        /// Converts the given object to a Scene2DPrimitive.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="sceneObject"></param>
        /// <returns></returns>
        private Scene2DPrimitive ConvertToPrimitive(uint id, PolygonObject sceneObject)
        {
            Polygon2D primitive = new Polygon2D();
            primitive.Id = id;

            // convert zoom range.
            ZoomRanges zoomRange = _zoomRanges.Get(sceneObject.ZoomRangeId);
            primitive.MaxZoom = zoomRange.MaxZoom;
            primitive.MinZoom = zoomRange.MinZoom;

            // convert image.
            StylePolygon style = _polygonStyles.Get(sceneObject.StyleId);
            primitive.Color = style.Color;
            primitive.Fill = style.Fill;
            primitive.Width = style.Width;

            // get the geo.
            KeyValuePair<double[], double[]> geo = _pointsIndex[(int)sceneObject.GeoId];
            primitive.X = geo.Key;
            primitive.Y = geo.Value;

            return new Scene2DPrimitive() { Layer = (int)sceneObject.Layer, Primitive = primitive };
        }

        #endregion

        #region Helper Objects

        private enum SceneObjectType
        {
            TextObject,
            PolygonObject,
            LineObject,
            LineTextObject,
            PointObject,
            IconObject
        }

        [ProtoContract]
        private abstract class SceneObject
        {
            [ProtoMember(1)]
            public SceneObjectType Enum { get; protected set; }

            [ProtoMember(2)]
            public uint Layer { get; set; }

            [ProtoMember(3)]
            public uint GeoId { get; set; }

            [ProtoMember(4)]
            public uint StyleId { get; set; }

            [ProtoMember(5)]
            public uint ZoomRangeId { get; set; }
        }

        [ProtoContract]
        private class TextObject : SceneObject
        {
            public TextObject()
            {
                this.Enum = SceneObjectType.TextObject;
            }

            public uint TextId { get; set; }
        }

        [ProtoContract]
        private class PolygonObject : SceneObject
        {
            public PolygonObject()
            {
                this.Enum = SceneObjectType.PolygonObject;
            }
        }

        [ProtoContract]
        private class LineObject : SceneObject
        {
            public LineObject()
            {
                this.Enum = SceneObjectType.LineObject;
            }
        }

        [ProtoContract]
        private class LineTextObject : SceneObject
        {
            public LineTextObject()
            {
                this.Enum = SceneObjectType.LineTextObject;
            }

            [ProtoMember(6)]
            public uint TextId { get; set; }
        }

        [ProtoContract]
        private class PointObject : SceneObject
        {
            public PointObject()
            {
                this.Enum = SceneObjectType.PointObject;
            }
        }

        [ProtoContract]
        private class IconObject : SceneObject
        {
            public IconObject()
            {
                this.Enum = SceneObjectType.IconObject;
            }
        }
        
        /// <summary>
        /// Zoom ranges.
        /// </summary>
        [ProtoContract]
        private class ZoomRanges
        {
            [ProtoMember(1)]
            public float MinZoom { get; set; }

            [ProtoMember(2)]
            public float MaxZoom { get; set; }

            public override int GetHashCode()
            {
                return this.MinZoom.GetHashCode() ^
                    this.MaxZoom.GetHashCode();
            }

            public override bool Equals(object obj)
            {
                if (obj is ZoomRanges)
                {
                    return (obj as ZoomRanges).MaxZoom == this.MaxZoom &&
                        (obj as ZoomRanges).MinZoom == this.MinZoom;
                }
                return false;
            }

            internal bool Contains(float zoom)
            {
                if (this.MinZoom > zoom || this.MaxZoom < zoom)
                { // outside of zoom bounds!
                    return false;
                }
                return true;
            }
        }

        /// <summary>
        /// Style for a point.
        /// </summary>
        [ProtoContract]
        private class StylePoint
        {
            public int Color { get; set; }

            public float Size { get; set; }

            public override int GetHashCode()
            {
                return this.Color.GetHashCode() ^
                    this.Size.GetHashCode();
            }

            public override bool Equals(object obj)
            {
                if (obj is StylePoint)
                {
                    return (obj as StylePoint).Color == this.Color &&
                        (obj as StylePoint).Size == this.Size;
                }
                return false;
            }
        }

        /// <summary>
        /// Style for a line.
        /// </summary>
        [ProtoContract]
        private class StyleLine
        {
            [ProtoMember(1)]
            public int Color { get; set; }

            [ProtoMember(3)]
            public float Width { get; set; }

            [ProtoMember(4)]
            public LineJoin LineJoin { get; set; }

            [ProtoMember(5, Options = global::ProtoBuf.MemberSerializationOptions.Packed)]
            public int[] Dashes { get; set; }

            public override int GetHashCode()
            {
                if (this.Dashes == null)
                {
                    return this.Color.GetHashCode() ^
                        this.Width.GetHashCode() ^
                        this.LineJoin.GetHashCode();
                }
                int hashcode = this.Color.GetHashCode() ^
                    this.Width.GetHashCode() ^
                    this.LineJoin.GetHashCode();
                foreach(int dash in this.Dashes)
                {
                    hashcode = hashcode ^ dash.GetHashCode();
                }
                return hashcode;
            }

            public override bool Equals(object obj)
            {
                if (obj is StyleLine)
                {
                    if ((obj as StyleLine).Color == this.Color &&
                        (obj as StyleLine).Width == this.Width &&
                        (obj as StyleLine).LineJoin == this.LineJoin)
                    {
                        if (this.Dashes != null)
                        {
                            if (this.Dashes.Length == (obj as StyleLine).Dashes.Length)
                            {
                                for (int idx = 0; idx < this.Dashes.Length; idx++)
                                {
                                    if (this.Dashes[idx] != (obj as StyleLine).Dashes[idx])
                                    {
                                        return false;
                                    }
                                }
                                return true;
                            }
                        }
                        else
                        {
                            return (obj as StyleLine).Dashes == null;
                        }
                    }
                }
                return false;
            }
        }
        
        /// <summary>
        /// Style for a point with text.
        /// </summary>
        [ProtoContract]
        private class StyleText
        {
            [ProtoMember(1)]
            public float Size { get; set; }

            [ProtoMember(2)]
            public int Color { get; set; }

            [ProtoMember(3)]
            public int? HaloColor { get; set; }

            [ProtoMember(4)]
            public int? HaloRadius { get; set; }

            [ProtoMember(5)]
            public string Font { get; set; }

            public override int GetHashCode()
            {
                if (this.Font == null)
                {
                    return this.Color.GetHashCode() ^
                        this.Size.GetHashCode() ^
                        this.HaloColor.GetHashCode() ^
                        this.HaloRadius.GetHashCode();
                }
                return this.Color.GetHashCode() ^
                    this.Size.GetHashCode() ^
                    this.HaloColor.GetHashCode() ^
                    this.HaloRadius.GetHashCode() ^
                    this.Font.GetHashCode();
            }

            public override bool Equals(object obj)
            {
                if (obj is StyleText)
                {
                    return (obj as StyleText).Size == this.Size &&
                        (obj as StyleText).Color == this.Color &&
                        (obj as StyleText).HaloRadius == this.HaloRadius &&
                        (obj as StyleText).HaloColor == this.HaloColor &&
                        (obj as StyleText).Font == this.Font;
                }
                return false;
            }
        }

        /// <summary>
        /// Style for a polygon.
        /// </summary>
        [ProtoContract]
        private class StylePolygon
        {
            [ProtoMember(1)]
            public float Width { get; set; }

            [ProtoMember(2)]
            public int Color { get; set; }

            [ProtoMember(3)]
            public bool Fill { get; set; }

            public override int GetHashCode()
            {
                return this.Width.GetHashCode() ^
                    this.Color.GetHashCode() ^
                    this.Fill.GetHashCode();
            }

            public override bool Equals(object obj)
            {
                if (obj is StylePolygon)
                {
                    return (obj as StylePolygon).Width == this.Width &&
                        (obj as StylePolygon).Color == this.Color &&
                        (obj as StylePolygon).Fill == this.Fill;
                }
                return false;
            }
        }

        #endregion

        /// <summary>
        /// Holds an R-tree serializer.
        /// </summary>
        private class RTreeSerializer : RTreeStreamSerializer<int>
        {
            /// <summary>
            /// Holds the simple scene.
            /// </summary>
            private Scene2DSimple _scene;

            /// <summary>
            /// Holds the compressed flag.
            /// </summary>
            private bool _compressed;

            /// <summary>
            /// Creates a new RTreeSerializer.
            /// </summary>
            /// <param name="scene"></param>
            /// <param name="compressed"></param>
            public RTreeSerializer(Scene2DSimple scene, bool compressed)
            {
                _compressed = compressed;
                _scene = scene;
            }

            /// <summary>
            /// Builds the protobuf runtime type model.
            /// </summary>
            /// <param name="typeModel"></param>
            protected override void BuildRuntimeTypeModel(RuntimeTypeModel typeModel)
            {
                typeModel.Add(typeof(RTreeLeaf), true);
            }

            /// <summary>
            /// Serializes one leaf.
            /// </summary>
            /// <param name="typeModel"></param>
            /// <param name="data"></param>
            /// <param name="boxes"></param>
            /// <returns></returns>
            protected override byte[] Serialize(RuntimeTypeModel typeModel, 
                List<int> data, List<BoxF2D> boxes)
            {
                int scaleFactor = 1000000;

                Dictionary<uint, int> addedPoint = new Dictionary<uint,int>();
                Dictionary<uint, int> addedPoints = new Dictionary<uint,int>();

                RTreeLeaf leafData = new RTreeLeaf();

                //leafData.PointsIndexes = new List<int>();
                leafData.PointsX = new List<long>();
                leafData.PointsY = new List<long>();

                leafData.IconPointId = new List<int>();
                leafData.IconImageId = new List<uint>();
                leafData.IconZoomRangeId = new List<uint>();

                leafData.LinePointsId= new List<int>();
                leafData.LineStyleId = new List<uint>();
                leafData.LineZoomRangeId = new List<uint>();

                leafData.LineTextPointsId = new List<int>();
                leafData.LineTextStyleId = new List<uint>();
                leafData.LineTextText = new List<string>();
                leafData.LineTextZoomRangeId = new List<uint>();

                leafData.PointPointId = new List<int>();
                leafData.PointStyleId = new List<uint>();
                leafData.PointZoomRangeId = new List<uint>();

                leafData.PolygonPointsId = new List<int>();
                leafData.PolygonStyleId = new List<uint>();
                leafData.PolygonZoomRangeId = new List<uint>();

                leafData.TextPointPointId = new List<int>();
                leafData.TextPointStyleId = new List<uint>();
                leafData.TextPointText = new List<string>();
                leafData.TextPointZoomRangeId = new List<uint>();

                foreach(int id in data)
                {
                    SceneObject sceneObject = _scene._sceneObjects[id];

                    int geoId = -1;
                    KeyValuePair<double, double> point;
                    KeyValuePair<double[], double[]> points;
                    switch (sceneObject.Enum)
                    {
                        case SceneObjectType.IconObject:
                            // get point.
                            point = _scene._pointIndex[(int)sceneObject.GeoId];

                            // set point data and keep id.
                            if(!addedPoint.TryGetValue(sceneObject.GeoId, out geoId))
                            { // the point was not added yet. 
                                geoId = leafData.PointsX.Count;
                                //leafData.PointsIndexes.Add(geoId);
                                leafData.PointsX.Add((long)(scaleFactor * point.Key));
                                leafData.PointsY.Add((long)(scaleFactor * point.Value));
                                addedPoint.Add(sceneObject.GeoId, geoId);
                            }
                            leafData.IconPointId.Add(geoId); // add point.

                            // add zoom range.
                            leafData.IconZoomRangeId.Add(sceneObject.ZoomRangeId);

                            // add image id.
                            leafData.IconImageId.Add(sceneObject.StyleId); 
                            break;
                        case SceneObjectType.PointObject:
                            // get point.
                            point = _scene._pointIndex[(int)sceneObject.GeoId];

                            // set point data and keep id.
                            if(!addedPoint.TryGetValue(sceneObject.GeoId, out geoId))
                            { // the point was not added yet. 
                                geoId = leafData.PointsX.Count;
                                leafData.PointsX.Add((long)(scaleFactor * point.Key));
                                leafData.PointsY.Add((long)(scaleFactor * point.Value));
                                //leafData.PointsIndexes.Add(leafData.PointsY.Count);
                                addedPoint.Add(sceneObject.GeoId, geoId);
                            }
                            leafData.PointPointId.Add(geoId);

                            // add zoom range.
                            leafData.PointZoomRangeId.Add(sceneObject.ZoomRangeId);

                            // add point style.
                            leafData.PointStyleId.Add(sceneObject.StyleId);
                            break;
                        case SceneObjectType.TextObject:
                            // get point.
                            point = _scene._pointIndex[(int)sceneObject.GeoId];

                            // set point data and keep id.
                            if (!addedPoint.TryGetValue(sceneObject.GeoId, out geoId))
                            { // the point was not added yet. 
                                geoId = leafData.PointsX.Count;
                                leafData.PointsX.Add((long)(scaleFactor * point.Key));
                                leafData.PointsY.Add((long)(scaleFactor * point.Value));
                                //leafData.PointsIndexes.Add(leafData.PointsY.Count);
                                addedPoint.Add(sceneObject.GeoId, geoId);
                            }
                            leafData.TextPointPointId.Add(geoId);

                            // add zoom range.
                            leafData.TextPointZoomRangeId.Add(sceneObject.ZoomRangeId);

                            // add point style.
                            leafData.TextPointStyleId.Add(sceneObject.StyleId);

                            // add text.
                            leafData.TextPointText.Add(
                                _scene._stringTable.Get((sceneObject as TextObject).TextId));
                            break;
                        case SceneObjectType.LineObject:
                            // get points.
                            points = _scene._pointsIndex[(int)sceneObject.GeoId];

                            // set points data and keep id.
                            if (!addedPoints.TryGetValue(sceneObject.GeoId, out geoId))
                            { // the point was not added yet. 
                                geoId = leafData.PointsX.Count;
                                leafData.PointsX.AddRange(points.Key.ConvertToLongArray(scaleFactor));
                                leafData.PointsY.AddRange(points.Value.ConvertToLongArray(scaleFactor));
                                //leafData.PointsIndexes.Add(leafData.PointsY.Count);
                                addedPoints.Add(sceneObject.GeoId, geoId);
                            }
                            leafData.LinePointsId.Add(geoId);

                            // add zoom range.
                            leafData.LineZoomRangeId.Add(sceneObject.ZoomRangeId);

                            // add point style.
                            leafData.LineStyleId.Add(sceneObject.StyleId);
                            break;
                        case SceneObjectType.LineTextObject:
                            // get points.
                            points = _scene._pointsIndex[(int)sceneObject.GeoId];

                            // set points data and keep id.
                            if (!addedPoints.TryGetValue(sceneObject.GeoId, out geoId))
                            { // the point was not added yet. 
                                geoId = leafData.PointsX.Count;
                                leafData.PointsX.AddRange(points.Key.ConvertToLongArray(scaleFactor));
                                leafData.PointsY.AddRange(points.Value.ConvertToLongArray(scaleFactor));
                                //leafData.PointsIndexes.Add(leafData.PointsY.Count);
                                addedPoint.Add(sceneObject.GeoId, geoId);
                            }
                            leafData.LineTextPointsId.Add(geoId);

                            // add zoom range.
                            leafData.LineTextZoomRangeId.Add(sceneObject.ZoomRangeId);

                            // add point style.
                            leafData.LineTextStyleId.Add(sceneObject.StyleId);

                            // add text.
                            leafData.LineTextText.Add(
                                _scene._stringTable.Get((sceneObject as LineTextObject).TextId));
                            break;
                        case SceneObjectType.PolygonObject:
                            // get points.
                            points = _scene._pointsIndex[(int)sceneObject.GeoId];

                            // set points data and keep id.
                            if (!addedPoints.TryGetValue(sceneObject.GeoId, out geoId))
                            { // the point was not added yet. 
                                geoId = leafData.PointsX.Count;
                                leafData.PointsX.AddRange(points.Key.ConvertToLongArray(scaleFactor));
                                leafData.PointsY.AddRange(points.Value.ConvertToLongArray(scaleFactor));
                                //leafData.PointsIndexes.Add(leafData.PointsY.Count);
                                addedPoints.Add(sceneObject.GeoId, geoId);
                            }
                            leafData.PolygonPointsId.Add(geoId);

                            // add zoom range.
                            leafData.PolygonZoomRangeId.Add(sceneObject.ZoomRangeId);

                            // add point style.
                            leafData.PolygonStyleId.Add(sceneObject.StyleId);
                            break;
                    }
                }

                // delta-encode.
                leafData.PointsX.EncodeDelta();
                leafData.PointsY.EncodeDelta();

                // serialize.
                MemoryStream stream = new MemoryStream();
                typeModel.Serialize(stream, leafData);
                byte[] serializedData = stream.ToArray();
                stream.Dispose();
                if (_compressed)
                { // compress.
                    serializedData = GZipStream.CompressBuffer(serializedData);
                }
                return serializedData;
            }

            /// <summary>
            /// Deserializes the given leaf.
            /// </summary>
            /// <param name="typeModel"></param>
            /// <param name="data"></param>
            /// <param name="boxes"></param>
            /// <returns></returns>
            protected override List<int> DeSerialize(RuntimeTypeModel typeModel, 
                byte[] data, out List<BoxF2D> boxes)
            {
                if (_compressed)
                { // decompress if needed.
                    data = GZipStream.UncompressBuffer(data);
                }

                List<int> dataLists = new List<int>();
                boxes = new List<BoxF2D>();

                int scaleFactor = 1000000;

                Dictionary<int, uint> points = new Dictionary<int, uint>();

                // Assume the following stuff already exists in the current scene:
                // - ZoomRanges
                // - Styles

                // deserialize the leaf data.
                RTreeLeaf leafData = typeModel.Deserialize(
                    new MemoryStream(data), null, typeof(RTreeLeaf)) as RTreeLeaf;

                // delta-decode.
                leafData.PointsX = leafData.PointsX.DecodeDelta();
                leafData.PointsY = leafData.PointsY.DecodeDelta();

                // loop over all points.
                for (int idx = 0; idx < leafData.PointPointId.Count; idx++)
                {
                    // add point.
                    int pointId = leafData.PointPointId[idx];
                    uint scenePointId = 0;
                    if (!points.TryGetValue(pointId, out scenePointId))
                    { // point was not yet added to the scene.
                        scenePointId = (uint)_scene._pointIndex.Count;
                        _scene._pointIndex.Add(new KeyValuePair<double, double>(
                            (double)leafData.PointsX[pointId] / (double)scaleFactor,
                            (double)leafData.PointsY[pointId] / (double)scaleFactor));
                        points.Add(pointId, scenePointId);
                    }
                    // build the point object.
                    PointObject pointObject = new PointObject();
                    pointObject.GeoId = scenePointId;
                    pointObject.StyleId = leafData.PointStyleId[idx];
                    pointObject.ZoomRangeId = leafData.PointZoomRangeId[idx];
                    // pointObject.Layer = ?
                    dataLists.Add(_scene._sceneObjects.Count);
                    _scene._sceneObjects.Add(pointObject);
                    // calculate box and add.
                    KeyValuePair<double, double> geoData = _scene._pointIndex[(int)pointObject.GeoId];
                    boxes.Add(new BoxF2D(new PointF2D(geoData.Key, geoData.Value)));
                }

                // loop over all text-points.
                for (int idx = 0; idx < leafData.TextPointPointId.Count; idx++)
                {
                    // add point.
                    int pointId = leafData.TextPointPointId[idx];
                    uint scenePointId = 0;
                    if (!points.TryGetValue(pointId, out scenePointId))
                    { // point was not yet added to the scene.
                        scenePointId = (uint)_scene._pointIndex.Count;
                        _scene._pointIndex.Add(new KeyValuePair<double, double>(
                            (double)leafData.PointsX[pointId] / (double)scaleFactor,
                            (double)leafData.PointsY[pointId] / (double)scaleFactor));
                        points.Add(pointId, scenePointId);
                    }
                    // build the point object.
                    TextObject textObject = new TextObject();
                    textObject.GeoId = scenePointId;
                    textObject.StyleId = leafData.TextPointStyleId[idx];
                    textObject.ZoomRangeId = leafData.TextPointZoomRangeId[idx];
                    textObject.TextId = _scene._stringTable.Add(leafData.TextPointText[idx]);
                    // textObject.Layer = ?
                    dataLists.Add(_scene._sceneObjects.Count);
                    _scene._sceneObjects.Add(textObject);
                    // calculate box and add.
                    KeyValuePair<double, double> geoData = _scene._pointIndex[(int)textObject.GeoId];
                    boxes.Add(new BoxF2D(new PointF2D(geoData.Key, geoData.Value)));
                }

                // loop over all icons.
                for (int idx = 0; idx < leafData.IconPointId.Count; idx++)
                {
                    // add point.
                    int pointId = leafData.IconPointId[idx];
                    uint scenePointId = 0;
                    if (!points.TryGetValue(pointId, out scenePointId))
                    { // point was not yet added to the scene.
                        scenePointId = (uint)_scene._pointIndex.Count;
                        _scene._pointIndex.Add(new KeyValuePair<double, double>(
                            (double)leafData.PointsX[pointId] / (double)scaleFactor,
                            (double)leafData.PointsY[pointId] / (double)scaleFactor));
                        points.Add(pointId, scenePointId);
                    }
                    // build the point object.
                    IconObject iconObject = new IconObject();
                    iconObject.GeoId = scenePointId;
                    iconObject.StyleId = leafData.IconImageId[idx];
                    iconObject.ZoomRangeId = leafData.IconZoomRangeId[idx];
                    // textObject.Layer = ?
                    dataLists.Add(_scene._sceneObjects.Count);
                    _scene._sceneObjects.Add(iconObject);
                    // calculate box and add.
                    KeyValuePair<double, double> geoData = _scene._pointIndex[(int)iconObject.GeoId];
                    boxes.Add(new BoxF2D(new PointF2D(geoData.Key, geoData.Value)));
                }

                // loop over all lines.
                for (int idx = 0; idx < leafData.LinePointsId.Count; idx++)
                {
                    // add point.
                    int pointsId = leafData.LinePointsId[idx];
                    int pointsEndId = 0;//leafData.PointsIndexes[pointsId];
                    uint scenePointId = 0;
                    if (!points.TryGetValue(pointsId, out scenePointId))
                    { // point was not yet added to the scene.
                        scenePointId = (uint)_scene._pointsIndex.Count;
                        _scene._pointsIndex.Add(new KeyValuePair<double[], double[]>(
                            leafData.PointsX.GetRange(pointsId, pointsEndId).ConvertFromLongArray(scaleFactor),
                            leafData.PointsY.GetRange(pointsId, pointsEndId).ConvertFromLongArray(scaleFactor)));
                        points.Add(pointsId, scenePointId);
                    }
                    // build the point object.
                    LineObject lineObject = new LineObject();
                    lineObject.GeoId = scenePointId;
                    lineObject.StyleId = leafData.LineStyleId[idx];
                    lineObject.ZoomRangeId = leafData.LineZoomRangeId[idx];
                    // textObject.Layer = ?
                    dataLists.Add(_scene._sceneObjects.Count);
                    _scene._sceneObjects.Add(lineObject);
                    // calculate box and add.
                    KeyValuePair<double[], double[]> geoData = _scene._pointsIndex[(int)lineObject.GeoId];
                    boxes.Add(new BoxF2D(geoData.Key, geoData.Value));
                }

                // loop over all polygons.
                for (int idx = 0; idx < leafData.PolygonPointsId.Count; idx++)
                {
                    // add point.
                    int pointsId = leafData.PolygonPointsId[idx];
                    int pointsEndId = 0; // leafData.PointsIndexes[pointsId];
                    uint scenePointId = 0;
                    if (!points.TryGetValue(pointsId, out scenePointId))
                    { // point was not yet added to the scene.
                        scenePointId = (uint)_scene._pointsIndex.Count;
                        _scene._pointsIndex.Add(new KeyValuePair<double[], double[]>(
                            leafData.PointsX.GetRange(pointsId, pointsEndId).ConvertFromLongArray(scaleFactor),
                            leafData.PointsY.GetRange(pointsId, pointsEndId).ConvertFromLongArray(scaleFactor)));
                        points.Add(pointsId, scenePointId);
                    }
                    // build the point object.
                    PolygonObject polygonObject = new PolygonObject();
                    polygonObject.GeoId = scenePointId;
                    polygonObject.StyleId = leafData.PolygonStyleId[idx];
                    polygonObject.ZoomRangeId = leafData.PolygonZoomRangeId[idx];
                    // textObject.Layer = ?
                    dataLists.Add(_scene._sceneObjects.Count);
                    _scene._sceneObjects.Add(polygonObject);
                    // calculate box and add.
                    KeyValuePair<double[], double[]> geoData = _scene._pointsIndex[(int)polygonObject.GeoId];
                    boxes.Add(new BoxF2D(geoData.Key, geoData.Value));
                }

                // loop over all line-texts.
                for (int idx = 0; idx < leafData.LineTextPointsId.Count; idx++)
                {
                    // add point.
                    int pointsId = leafData.LineTextPointsId[idx];
                    int pointsEndId = 0; // leafData.PointsIndexes[pointsId];
                    uint scenePointId = 0;
                    if (!points.TryGetValue(pointsId, out scenePointId))
                    { // point was not yet added to the scene.
                        scenePointId = (uint)_scene._pointsIndex.Count;
                        _scene._pointsIndex.Add(new KeyValuePair<double[], double[]>(
                            leafData.PointsX.GetRange(pointsId, pointsEndId).ConvertFromLongArray(scaleFactor),
                            leafData.PointsY.GetRange(pointsId, pointsEndId).ConvertFromLongArray(scaleFactor)));
                        points.Add(pointsId, scenePointId);
                    }
                    // build the point object.
                    LineTextObject lineTextObject = new LineTextObject();
                    lineTextObject.GeoId = scenePointId;
                    lineTextObject.StyleId = leafData.LineTextStyleId[idx];
                    lineTextObject.ZoomRangeId = leafData.LineTextZoomRangeId[idx];
                    lineTextObject.TextId = _scene._stringTable.Add(leafData.LineTextText[idx]);
                    // textObject.Layer = ?
                    dataLists.Add(_scene._sceneObjects.Count);
                    _scene._sceneObjects.Add(lineTextObject);
                    // calculate box and add.
                    KeyValuePair<double[], double[]> geoData = _scene._pointsIndex[(int)lineTextObject.GeoId];
                    boxes.Add(new BoxF2D(geoData.Key, geoData.Value));
                }
                return dataLists;
            }

            public override string VersionString
            {
                get { return "RTreeScene"; }
            }
        }

        /// <summary>
        /// Scene 2D primitive source.
        /// </summary>
        private class Scene2DPrimitivesSource : IScene2DPrimitivesSource
        {
            /// <summary>
            /// Holds the serializer.
            /// </summary>
            private readonly ISpatialIndexReadonly<int> _index;

            /// <summary>
            /// Holds the scene index.
            /// </summary>
            private readonly SceneIndex _sceneIndex;

            /// <summary>
            /// Creates a new primitives source index.
            /// </summary>
            /// <param name="index"></param>
            /// <param name="sceneIndex"></param>
            public Scene2DPrimitivesSource(ISpatialIndexReadonly<int> index,
                SceneIndex sceneIndex)
            {
                _index = index;
                _sceneIndex = sceneIndex;
            }

            /// <summary>
            /// Adds the objects in the given view to the given scene for the given zoomFactor.
            /// </summary>
            /// <param name="scene"></param>
            /// <param name="view"></param>
            /// <param name="zoomFactor"></param>
            public void Get(Scene2DSimple scene, View2D view, float zoomFactor)
            {
                _index.Get(view.OuterBox);
            }

            /// <summary>
            /// Removes all cached info from this scene.
            /// </summary>
            public void Clear()
            {

            }

            /// <summary>
            /// Disposes all resources associated with this primitive source.
            /// </summary>
            public void Dispose()
            {

            }
        }

        /// <summary>
        /// Scene index.
        /// </summary>
        [ProtoContract]
        private class SceneIndex
        {
            /// <summary>
            /// Holds the point styles.
            /// </summary>
            [ProtoMember(1)]
            public StylePoint[] PointStyles { get; set; }

            /// <summary>
            /// Holds the text styles.
            /// </summary>
            [ProtoMember(2)]
            public StyleText[] TextStyles { get; set; }

            /// <summary>
            /// Holds the line styles.
            /// </summary>
            [ProtoMember(3)]
            public StyleLine[] LineStyles { get; set; }

            /// <summary>
            /// Holds the polygon styles.
            /// </summary>
            [ProtoMember(4)]
            public StylePolygon[] PolygonStyles { get; set; }

            /// <summary>
            /// Holds the zoom ranges.
            /// </summary>
            [ProtoMember(5)]
            public ZoomRanges[] ZoomRanges { get; set; }
        }

        /// <summary>
        /// Serializable leaf.
        /// </summary>
        [ProtoContract]
        private class RTreeLeaf
        {
            [ProtoMember(1, Options = global::ProtoBuf.MemberSerializationOptions.Packed, DataFormat = global::ProtoBuf.DataFormat.ZigZag)]
            public List<long> PointsX { get; set; }

            [ProtoMember(2, Options = global::ProtoBuf.MemberSerializationOptions.Packed, DataFormat = global::ProtoBuf.DataFormat.ZigZag)]
            public List<long> PointsY { get; set; }


            [ProtoMember(3, Options = global::ProtoBuf.MemberSerializationOptions.Packed)]
            public List<uint> PointStyleId { get; set; }

            [ProtoMember(4, Options = global::ProtoBuf.MemberSerializationOptions.Packed)]
            public List<int> PointPointId { get; set; }

            [ProtoMember(5, Options = global::ProtoBuf.MemberSerializationOptions.Packed)]
            public List<uint> PointZoomRangeId { get; set; }


            [ProtoMember(6, Options = global::ProtoBuf.MemberSerializationOptions.Packed)]
            public List<uint> TextPointStyleId { get; set; }

            [ProtoMember(7, Options = global::ProtoBuf.MemberSerializationOptions.Packed)]
            public List<int> TextPointPointId { get; set; }

            [ProtoMember(8)]
            public List<string> TextPointText { get; set; }

            [ProtoMember(9, Options = global::ProtoBuf.MemberSerializationOptions.Packed)]
            public List<uint> TextPointZoomRangeId { get; set; }


            [ProtoMember(10, Options = global::ProtoBuf.MemberSerializationOptions.Packed)]
            public List<uint> LineStyleId { get; set; }

            [ProtoMember(11, Options = global::ProtoBuf.MemberSerializationOptions.Packed)]
            public List<int> LinePointsId { get; set; }

            [ProtoMember(12, Options = global::ProtoBuf.MemberSerializationOptions.Packed)]
            public List<uint> LineZoomRangeId { get; set; }


            [ProtoMember(13, Options = global::ProtoBuf.MemberSerializationOptions.Packed)]
            public List<uint> PolygonStyleId { get; set; }

            [ProtoMember(14, Options = global::ProtoBuf.MemberSerializationOptions.Packed)]
            public List<int> PolygonPointsId { get; set; }

            [ProtoMember(15, Options = global::ProtoBuf.MemberSerializationOptions.Packed)]
            public List<uint> PolygonZoomRangeId { get; set; }


            [ProtoMember(16, Options = global::ProtoBuf.MemberSerializationOptions.Packed)]
            public List<uint> LineTextStyleId { get; set; }

            [ProtoMember(17, Options = global::ProtoBuf.MemberSerializationOptions.Packed)]
            public List<int> LineTextPointsId { get; set; }

            [ProtoMember(18)]
            public List<string> LineTextText { get; set; }

            [ProtoMember(19, Options = global::ProtoBuf.MemberSerializationOptions.Packed)]
            public List<uint> LineTextZoomRangeId { get; set; }


            [ProtoMember(20, Options = global::ProtoBuf.MemberSerializationOptions.Packed)]
            public List<uint> IconImageId { get; set; }

            [ProtoMember(21, Options = global::ProtoBuf.MemberSerializationOptions.Packed)]
            public List<int> IconPointId { get; set; }

            [ProtoMember(22, Options = global::ProtoBuf.MemberSerializationOptions.Packed)]
            public List<uint> IconZoomRangeId { get; set; }
        }
    }
}