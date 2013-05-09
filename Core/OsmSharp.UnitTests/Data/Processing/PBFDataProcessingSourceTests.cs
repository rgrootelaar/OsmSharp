﻿// OsmSharp - OpenStreetMap tools & library.
// Copyright (C) 2012 Abelshausen Ben
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

using NUnit.Framework;
using System.Reflection;
using OsmSharp.Osm.Data.PBF.Processor;
using OsmSharp.Osm.Data.Streams;

namespace OsmSharp.UnitTests.Data.Processing
{
    /// <summary>
    /// Contains tests for the PBF osm streams.
    /// </summary>
    [TestFixture]
    public class PBFOsmStreamsTests
    {
        /// <summary>
        /// A regression test on resetting a PBF osm stream.
        /// </summary>
        [Test]
        public void PBFOsmStreamReaderReset()
        {
            // generate the source.
            var source = new PBFOsmStreamReader(
                Assembly.GetExecutingAssembly().GetManifestResourceStream(
                    "OsmSharp.UnitTests.api.osm.pbf"));

            // pull the data out.
            var target = new OsmStreamWriterEmpty();
            target.RegisterSource(source);
            target.Pull();

            // reset the source.
            if (source.CanReset)
            {
                source.Reset();

                // pull the data again.
                target.Pull();
            }
        }
    }
}