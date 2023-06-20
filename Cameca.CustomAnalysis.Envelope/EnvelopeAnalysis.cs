using Cameca.CustomAnalysis.Interface;
using Cameca.CustomAnalysis.Utilities;
using Cameca.CustomAnalysis.Utilities.Legacy;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Windows;
using System;
using System.Windows.Media;
using Cameca.CustomAnalysis.Envelope;

namespace Cameca.CustomAnalysis.Envelope;

internal class EnvelopeAnalysis : ICustomAnalysis<EnvelopeOptions>
{
    //Constants
    public const int ROUNDING_LENGTH = 2;

    //Node ID for resolving services
    public Guid ID { get; set; }

    /*
     * Service Injection into the constructor
     */
    private readonly IMassSpectrumRangeManagerProvider _massSpectrumRangeManagerProvider;

    public EnvelopeAnalysis(IMassSpectrumRangeManagerProvider massSpectrumRangeManagerProvider)
    {
        _massSpectrumRangeManagerProvider = massSpectrumRangeManagerProvider;
    }


    /// <summary>
    /// Main custom analysis execution method.
    /// </summary>
    /// <remarks>
    /// Use <paramref name="ionData"/> as the data source for your calculation.
    /// Configurability in AP Suite can be implemented by creating editable properties in the options object. Access here with <paramref name="options"/>.
    /// Render your results with a variety of charts or tables by passing your final data to <see cref="IViewBuilder"/> methods.
    /// e.g. Create a histogram by calling <see cref="IViewBuilder.AddHistogram2D"/> on <paramref name="viewBuilder"/>
    /// </remarks>
    /// <param name="ionData">Provides access to mass, position, and other ion data.</param>
    /// <param name="options">Configurable options displayed in the property editor.</param>
    /// <param name="viewBuilder">Defines how the result will be represented in AP Suite</param>
    public void Run(IIonData ionData, EnvelopeOptions options, IViewBuilder viewBuilder)
    {
        //Check for good input
        (bool didParse, List<byte> ranges) = ParseRangeInput(options.RangeStr, _massSpectrumRangeManagerProvider);
        if (!didParse)
        {
            MessageBox.Show("Bad Range input. Separate indexes with spaces");
            return;
        }

        //output string for the text
        StringBuilder outBuilder = new();
        outBuilder.AppendLine($"Ions: {ionData.IonCount}\n");

        /*
         * Composition section
         */
        outBuilder.AppendLine(CalculateComposition(ionData, viewBuilder));

        /*
         * XYZ Limits Section
         */
        var message = GetLimits(ionData, viewBuilder, options.AtomSeparation);
        if (message == null)
            return;
        outBuilder.AppendLine(message);

        /*
         * Cluster Section
         */
        (var selectedIonList, var ionIndexList, var ionToIonTypeDict, message, var unrangedCount) = GetSelectedIons(ionData, ranges);
        outBuilder.AppendLine(message);
        var adjacencyList = CreateAdjacencyList(selectedIonList, options.AtomSeparation, ionData);
        //cluster list is a list of lists of ints. Each list is a cluster, and inside the cluster list, each int is the index that corresponds to 
        //the ion in selectedIonList at that index
        var clusterList = GraphToClusters(ionIndexList, adjacencyList);
        outBuilder.AppendLine($"{clusterList.Count} clusters found containing {selectedIonList.Count} solute atoms\n");
        message = RemoveSmallClusters(clusterList, options.MinAtomsPerCluster);
        outBuilder.AppendLine(message);

        /*
         * Envelope Section
         */
        var numToNameDict = GetNumToNameDict(ionData);
        var gyrationData = InitGyrationColumns(ranges, numToNameDict);
        (var envelopeList, message, var envelopeIonList, var envelopeBodyList) = CreateEnvelopes(clusterList, selectedIonList, ionToIonTypeDict, numToNameDict, options.GridResolution, options.FillInGrid, gyrationData);
        outBuilder.AppendLine(message);

        /*
         * Final Cluster Composition
         */
        outBuilder.AppendLine(CalculateEnvelopesComposition(envelopeList, viewBuilder, ionData, numToNameDict, unrangedCount, gyrationData, ranges.Count));

        viewBuilder.AddTable("Cluster COM and Gyration Info", gyrationData.DefaultView);

        /*
         * Graphing the clusters
         */
        //Point View
        Create3DGraph(envelopeIonList, viewBuilder);

        //Voxel View
        var envelopeOffsets = GetEnvelopeOffsets(envelopeIonList, options.GridResolution);
        GraphEnvelopeBodies(envelopeBodyList, options.GridResolution, viewBuilder, envelopeOffsets);

        viewBuilder.AddText("Envelope Output", outBuilder.ToString(), new TextBoxOptions());
    }

    /// <summary>
    /// Initializes a DataTable object for the Gyration Table, and returns it for further editing
    /// </summary>
    /// <param name="ranges">A list of the ranges that the user has selected</param>
    /// <param name="numToNameDict">A dictionary mapping the type "id" of an ion to the string name of it</param>
    /// <returns>A DataTable object initialized with the colummns required for this run of the program</returns>
    private static DataTable InitGyrationColumns(List<byte> ranges, Dictionary<byte, string> numToNameDict)
    {
        var gyrationData = new DataTable();
        gyrationData.Columns.Add("Atoms");
        gyrationData.Columns.Add("xbar");
        gyrationData.Columns.Add("ybar");
        gyrationData.Columns.Add("zbar");
        gyrationData.Columns.Add("lgx");
        gyrationData.Columns.Add("lgy");
        gyrationData.Columns.Add("lgz");
        gyrationData.Columns.Add("lg");

        //add a column for each range selected by user
        foreach (byte range in ranges)
        {
            gyrationData.Columns.Add($"lg[{numToNameDict[range]}]");
        }
        //add a column for each type of ion ranged
        foreach (string name in numToNameDict.Values)
        {
            gyrationData.Columns.Add($"{name}[ion]");
            gyrationData.Columns.Add($"{name}[conc]");
            gyrationData.Columns.Add($"{name}[error]");
        }

        gyrationData.Columns.Add("Ions");
        gyrationData.Columns.Add("Ions per grid");

        return gyrationData;
    }

    /// <summary>
    /// A helper method to calculate the offset of each envelope releative to the overall grid, in order to graph them all correctly
    /// </summary>
    /// <param name="envelopeIonList">A list of lists of vectors, where each list of vectors is an envelope containing ions</param>
    /// <param name="voxelSize">The size of the voxels being graphed, should correspond to the gridSize from the user</param>
    /// <returns>A list of vectors containing the offsets of the clusters to be graphed.</returns>
    private static List<Vector3> GetEnvelopeOffsets(List<List<Vector3>> envelopeIonList, float voxelSize)
    {
        List<Vector3> envelopeOffsets = new();

        foreach (List<Vector3> envelopeIons in envelopeIonList)
        {
            Vector3 min = new(float.MaxValue);
            foreach (Vector3 ion in envelopeIons)
            {
                if (ion.X < min.X) min.X = ion.X;
                if (ion.Y < min.Y) min.Y = ion.Y;
                if (ion.Z < min.Z) min.Z = ion.Z;
            }

            min /= voxelSize;
            min.X = (int)min.X;
            min.Y = (int)min.Y;
            min.Z = (int)min.Z;
            min *= voxelSize;

            envelopeOffsets.Add(min);
        }

        return envelopeOffsets;
    }

    /// <summary>
    /// Method that takes all the envelope bodies and graphs them in voxel format to see the grid elements being selected from the envelope
    /// </summary>
    /// <param name="envelopeBodies">A list of 3D boolean arrays containing which voxels are filled in</param>
    /// <param name="voxelSize">A float value containing the size of the user-specified voxel size (cube)</param>
    /// <param name="viewBuilder">An IViewBuilder object to render the graph being created here</param>
    /// <param name="envelopeOffsets">A list of vectors that correspond to the offset (distance from (0,0,0) ) to the start each cluster</param>
    private static void GraphEnvelopeBodies(List<bool[,,]> envelopeBodies, float voxelSize, IViewBuilder viewBuilder, List<Vector3> envelopeOffsets)
    {
        var graph = viewBuilder.AddChart3D("Envelope Bodies");
        //Random object is used for coloring each envelope in this graph with a (probably) different color
        Random random = new(0);

        for (int i = 0; i < envelopeBodies.Count; i++)
        {
            var envelopeOffset = envelopeOffsets[i];
            var envelopeBody = envelopeBodies[i];
            GraphEnvelopeBody(envelopeBody, voxelSize, graph, random, envelopeOffset);
        }
    }

    /// <summary>
    /// Method responsible for graphing an individual envelope and the voxels selected from that envelope's cluster
    /// </summary>
    /// <param name="envelopeBody">A 3D boolean array mapping which voxels are to be filled in</param>
    /// <param name="voxelSize">A float value for the size of each voxel cube</param>
    /// <param name="graph">A Chart3DBuilder object that corresponds to the given graph this envelope is to be graphed on</param>
    /// <param name="random">A random object used to provide coloring information for each cluster</param>
    /// <param name="envelopeOffset">A vector that corresponds to the offset (distance from (0,0,0) ) to the start of this cluster</param>
    private static void GraphEnvelopeBody(bool[,,] envelopeBody, float voxelSize, IChart3DBuilder graph, Random random, Vector3 envelopeOffset)
    {
        List<float> xPosList = new();
        List<float> yPosList = new();
        List<float> zPosList = new();
        List<int> indexList = new();

        for (int i = 0; i < envelopeBody.GetLength(0); i++)
        {
            float xPos = (voxelSize * i) + envelopeOffset.X;
            for (int j = 0; j < envelopeBody.GetLength(1); j++)
            {
                float yPos = (voxelSize * j) + envelopeOffset.Y;
                for (int k = 0; k < envelopeBody.GetLength(2); k++)
                {
                    float zPos = (voxelSize * k) + envelopeOffset.Z;

                    //if this voxel is filled in we do the calculations
                    if (envelopeBody[i, j, k])
                    {
                        int startIndex = xPosList.Count;

                        //add points                                                                                       OFFSET
                        AddPointToList(xPos, yPos, zPos, xPosList, yPosList, zPosList);                                     // 0
                        AddPointToList(xPos, yPos, zPos + voxelSize, xPosList, yPosList, zPosList);                         // 1
                        AddPointToList(xPos, yPos + voxelSize, zPos, xPosList, yPosList, zPosList);                         // 2
                        AddPointToList(xPos, yPos + voxelSize, zPos + voxelSize, xPosList, yPosList, zPosList);             // 3
                        AddPointToList(xPos + voxelSize, yPos, zPos, xPosList, yPosList, zPosList);                         // 4
                        AddPointToList(xPos + voxelSize, yPos, zPos + voxelSize, xPosList, yPosList, zPosList);             // 5
                        AddPointToList(xPos + voxelSize, yPos + voxelSize, zPos, xPosList, yPosList, zPosList);             // 6
                        AddPointToList(xPos + voxelSize, yPos + voxelSize, zPos + voxelSize, xPosList, yPosList, zPosList); // 7

                        //make triangles
                        //front
                        indexList.Add(startIndex + 0);
                        indexList.Add(startIndex + 2);
                        indexList.Add(startIndex + 6);
                        indexList.Add(startIndex + 0);
                        indexList.Add(startIndex + 4);
                        indexList.Add(startIndex + 6);

                        //back
                        indexList.Add(startIndex + 1);
                        indexList.Add(startIndex + 3);
                        indexList.Add(startIndex + 7);
                        indexList.Add(startIndex + 1);
                        indexList.Add(startIndex + 5);
                        indexList.Add(startIndex + 7);

                        //top
                        indexList.Add(startIndex + 2);
                        indexList.Add(startIndex + 3);
                        indexList.Add(startIndex + 7);
                        indexList.Add(startIndex + 2);
                        indexList.Add(startIndex + 6);
                        indexList.Add(startIndex + 7);

                        //bottom
                        indexList.Add(startIndex + 0);
                        indexList.Add(startIndex + 1);
                        indexList.Add(startIndex + 5);
                        indexList.Add(startIndex + 0);
                        indexList.Add(startIndex + 4);
                        indexList.Add(startIndex + 5);

                        //left
                        indexList.Add(startIndex + 0);
                        indexList.Add(startIndex + 2);
                        indexList.Add(startIndex + 3);
                        indexList.Add(startIndex + 0);
                        indexList.Add(startIndex + 1);
                        indexList.Add(startIndex + 3);

                        //right
                        indexList.Add(startIndex + 4);
                        indexList.Add(startIndex + 6);
                        indexList.Add(startIndex + 7);
                        indexList.Add(startIndex + 4);
                        indexList.Add(startIndex + 5);
                        indexList.Add(startIndex + 7);
                    }
                }
            }
        }

        //Color randColor = Color.FromRgb((byte)random.Next(255), (byte)random.Next(255), (byte)random.Next(255));
        var randColor = random.NextColor();
        graph.AddSurface(xPosList.ToArray(), yPosList.ToArray(), zPosList.ToArray(), indexList.ToArray(), randColor);
    }

    /// <summary>
    /// Helper method to add an (x,y,z) point to the lists for rendering. Makes the code using this method more readable. 
    /// </summary>
    /// <param name="x">x coordinate value</param>
    /// <param name="y">y cooridnate value</param>
    /// <param name="z">z coordinate value</param>
    /// <param name="xList">list object of x values</param>
    /// <param name="yList">list object of y values</param>
    /// <param name="zList">list object of z values</param>
    private static void AddPointToList(float x, float y, float z, List<float> xList, List<float> yList, List<float> zList)
    {
        xList.Add(x);
        yList.Add(y);
        zList.Add(z);
    }

    /// <summary>
    /// Method to graph all of the points in a given list of envelopes (as pixels/points)
    /// </summary>
    /// <param name="envelopeIonList">List of lists of vectors, where each list of vector corresponds to an envelope</param>
    /// <param name="viewBuilder">IViewBuilder object to graph the given points</param>
    private static void Create3DGraph(List<List<Vector3>> envelopeIonList, IViewBuilder viewBuilder)
    {
        var chart = viewBuilder.AddChart3D("Envelopes");
        Random random = new(0);
        foreach (List<Vector3> envelope in envelopeIonList)
        {
            (var xVals, var yVals, var zVals) = GetPointsFromEnvelope(envelope);
            //Color randomColor = Color.FromArgb(255, (byte)random.Next(255), (byte)random.Next(255), (byte)random.Next(255));
            var randomColor = random.NextColor();
            chart.AddPoints(xVals, yVals, zVals, randomColor);
        }
    }

    /// <summary>
    /// Helper method to convert an envelope (list of vectors) into 3 float[] for graphing purposes
    /// </summary>
    /// <param name="envelope">List of vectors representing the points in this envelope</param>
    /// <returns>Three float arrays corresponding to the x, y, and z values of the points in this envelope</returns>
    private static (float[], float[], float[]) GetPointsFromEnvelope(List<Vector3> envelope)
    {
        float[] xVals = new float[envelope.Count];
        float[] yVals = new float[envelope.Count];
        float[] zVals = new float[envelope.Count];

        int index = 0;
        foreach (Vector3 ion in envelope)
        {
            xVals[index] = ion.X;
            yVals[index] = ion.Y;
            zVals[index] = ion.Z;
            index++;
        }

        return (xVals, yVals, zVals);
    }

    /// <summary>
    /// Method to calculate the composition of everything NOT in the envelopes that have been found so far.
    /// </summary>
    /// <param name="envelopeList">List of dictionaries, where each dictionary represents an envelope, containing the types of ions and the counts of them</param>
    /// <param name="viewBuilder">Viewbuilder object to add a chart to for displaying the information</param>
    /// <param name="ionData">IonData object to get information on the ions/atoms</param>
    /// <param name="numToNameDict">Dictionary that maps the byte "id" of an atom to the name corresponding to it</param>
    /// <param name="unrangedIonCount">integer value of the amount of ions that were unranged (id = 255)</param>
    /// <returns>A formatted string to display the envelope composition information</returns>
    private static string CalculateEnvelopesComposition(List<Dictionary<byte, int>> envelopeList, IViewBuilder viewBuilder, IIonData ionData, Dictionary<byte, string> numToNameDict, int unrangedIonCount, DataTable gyrationTable, int selectedIons)
    {
        StringBuilder sb = new();
        Dictionary<byte, ulong> allEnvelopes = new();
        ulong totalAtomCount = ionData.IonCount - (ulong)unrangedIonCount;
        var allIonsDict = ionData.GetIonTypeCounts();
        var nameToNumDict = numToNameDict.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);
        List<CompositionRow> compositionRows = new();

        var ionTypes = ionData.Ions.ToList();
        for (byte b = 0; b < ionTypes.Count; b++)
        {
            allEnvelopes.Add(b, 0);
        }
        foreach (KeyValuePair<IIonTypeInfo, ulong> kvp in allIonsDict)
        {
            allEnvelopes[nameToNumDict[kvp.Key.Name]] = kvp.Value;
        }

        //go through counted envelopes / clusters
        foreach (Dictionary<byte, int> envelope in envelopeList)
        {
            foreach (KeyValuePair<byte, int> idToCount in envelope)
            {
                allEnvelopes[idToCount.Key] -= (ulong)idToCount.Value;
                totalAtomCount -= (ulong)idToCount.Value;
            }
        }

        sb.AppendLine($"{envelopeList.Count} clusters found in {ionData.IonCount} atoms\n");

        sb.AppendLine($"Total atoms in matrix (included deleted clusters): {totalAtomCount}");

        var sortedKeys = allEnvelopes.Keys.ToList();
        sortedKeys.Sort();

        List<Object> matrixGyrationEntry = new();
        //                                                        these 6 are for COM and gyration
        matrixGyrationEntry.AddRange(new Object[] { "matrix", totalAtomCount, "", "", "", "", "", "" });
        for (int i = 0; i < selectedIons; i++)
            matrixGyrationEntry.Add("");

        foreach (byte key in sortedKeys)
        {
            double percent = ((float)allEnvelopes[key] / totalAtomCount);
            double error = Math.Sqrt(percent * (1.0 - percent) / totalAtomCount);
            string errorStr = error.ToString($"p{ROUNDING_LENGTH + 1}");
            compositionRows.Add(new CompositionRow(numToNameDict[key], allEnvelopes[key], percent.ToString($"p{ROUNDING_LENGTH}"), errorStr));
            sb.AppendLine($"{numToNameDict[key]} ions: {allEnvelopes[key]}, composition = {percent.ToString($"p{ROUNDING_LENGTH}")} +/- {errorStr}");

            matrixGyrationEntry.AddRange(new Object[] { allEnvelopes[key], percent.ToString($"p{ROUNDING_LENGTH}"), errorStr });
        }
        compositionRows.Add(new CompositionRow("Total", totalAtomCount, "", ""));

        matrixGyrationEntry.Add(totalAtomCount);

        gyrationTable.Rows.Add();
        gyrationTable.Rows.Add();
        gyrationTable.Rows.Add(matrixGyrationEntry.ToArray());

        viewBuilder.AddTable("Matrix Composition", compositionRows);
        return sb.ToString();
    }


    //needs refactoring (maybe) way too many parameters
    /// <summary>
    /// Method for the process of creating the envelopes from the clusters
    /// </summary>
    /// <param name="clusterList">List of clusters, where a cluster is a list of ions</param>
    /// <param name="ionPositionList">list of vectors of the ions that have been selected</param>
    /// <param name="ionToIonTypeDict">Dictionary mapping vectors (ions) to their corresponding type (byte)</param>
    /// <param name="typeToNameDict">Dictionary mapping an ion's type (byte) to their name (string)</param>
    /// <param name="gridSize">float value of the size (in nm) of the grid to create the envelope with</param>
    /// <param name="fillInGrid">boolean value on whether or not to fill in gaps of the envelope</param>
    /// <returns>A list of dictionaries, where each entry in list is an envelope, and the dictionary maps the type of ion to the count</returns>
    private static (List<Dictionary<byte, int>>, string, List<List<Vector3>>, List<bool[,,]>) CreateEnvelopes(List<List<int>> clusterList, List<Vector3> ionPositionList, Dictionary<Vector3, byte> ionToIonTypeDict, Dictionary<byte, string> typeToNameDict, float gridSize, bool fillInGrid, DataTable gyrationTable)
    {
        StringBuilder sb = new();
        List<Dictionary<byte, int>> toRet = new();
        List<List<Vector3>> envelopeIonList = new();
        List<bool[,,]> envelopeBodyList = new();

        //each cluster
        foreach (List<int> cluster in clusterList)
        {
            List<Vector3> thisEnvelopeList = new();

            List<Object> gyrationRowList = new();
            Dictionary<byte, int> rangeTypeCount = new();
            Dictionary<byte, Vector3> ionToGyrationDict = new();
            Dictionary<byte, Vector3> ionToCOMDict = new();
            foreach (byte type in typeToNameDict.Keys)
            {
                ionToGyrationDict.Add(type, new Vector3(0));
                rangeTypeCount.Add(type, 0);
                ionToCOMDict.Add(type, new Vector3(0));
            }

            //find extents and center of mass first
            Vector3 min = new(float.MaxValue);
            Vector3 max = new(float.MinValue);
            Vector3 centerOfMass = new(0);
            foreach (int ionIndex in cluster)
            {
                Vector3 currIon = ionPositionList[ionIndex];

                if (currIon.X < min.X)
                    min.X = currIon.X;
                if (currIon.X > max.X)
                    max.X = currIon.X;
                if (currIon.Y < min.Y)
                    min.Y = currIon.Y;
                if (currIon.Y > max.Y)
                    max.Y = currIon.Y;
                if (currIon.Z < min.Z)
                    min.Z = currIon.Z;
                if (currIon.Z > max.Z)
                    max.Z = currIon.Z;

                centerOfMass += currIon;

                rangeTypeCount[ionToIonTypeDict[currIon]]++;
            }
            centerOfMass /= cluster.Count;

            //Center of mass
            foreach (int ionIndex in cluster)
            {
                Vector3 currIon = ionPositionList[ionIndex];

                ionToCOMDict[ionToIonTypeDict[currIon]] += currIon;
            }
            foreach (byte comIndex in ionToCOMDict.Keys)
            {
                ionToCOMDict[comIndex] /= rangeTypeCount[comIndex];
            }
            //radius of gyration
            Vector3 radiusGyration = new(0);
            foreach (int ionIndex in cluster)
            {
                Vector3 currIon = ionPositionList[ionIndex];
                radiusGyration += (currIon - centerOfMass) * (currIon - centerOfMass);

                ionToGyrationDict[ionToIonTypeDict[currIon]] += (currIon - ionToCOMDict[ionToIonTypeDict[currIon]]) * (currIon - ionToCOMDict[ionToIonTypeDict[currIon]]);
            }
            double totalGyration = radiusGyration.X + radiusGyration.Y + radiusGyration.Z;
            totalGyration = Math.Sqrt(totalGyration / cluster.Count);
            radiusGyration = Vector3.SquareRoot(radiusGyration / cluster.Count);

            //remove ion types from dictionaries if they are emtpy
            foreach (byte type in typeToNameDict.Keys)
            {
                if (ionToGyrationDict[type] == new Vector3(0))
                    ionToGyrationDict.Remove(type);
                if (ionToCOMDict[type] == new Vector3(0))
                    ionToCOMDict.Remove(type);
            }

            //first part of gyration table info
            gyrationRowList.AddRange(new object[] { cluster.Count, centerOfMass.X, centerOfMass.Y, centerOfMass.Z, radiusGyration.X, radiusGyration.Y, radiusGyration.Z, totalGyration });
            //ranges selected
            foreach (KeyValuePair<byte, Vector3> ionTypeGyrationPair in ionToGyrationDict)
            {
                var type = ionTypeGyrationPair.Key;
                var gyration = ionTypeGyrationPair.Value;
                totalGyration = gyration.X + gyration.Y + gyration.Z;
                totalGyration = Math.Sqrt(totalGyration / rangeTypeCount[type]);
                gyrationRowList.Add(totalGyration);
            }


            //add extra space
            min -= new Vector3(gridSize);
            max += new Vector3(gridSize);

            //create 3d array
            Vector3 diff = max - min;
            int numX = ((int)(diff / gridSize).X) + 1;
            int numY = ((int)(diff / gridSize).Y) + 1;
            int numZ = ((int)(diff / gridSize).Z) + 1;

            //this creates the body of the envelope
            bool[,,] envelopeBody = new bool[numX, numY, numZ];
            foreach (int ionIndex in cluster)
            {
                Vector3 thisIon = ionPositionList[ionIndex];
                thisIon.X = (thisIon.X - min.X) / gridSize;
                thisIon.Y = (thisIon.Y - min.Y) / gridSize;
                thisIon.Z = (thisIon.Z - min.Z) / gridSize;
                envelopeBody[(int)thisIon.X, (int)thisIon.Y, (int)thisIon.Z] = true;
            }

            if (fillInGrid)
            {
                //xyz
                for (int i = 0; i < envelopeBody.GetLength(0); i++)
                {
                    for (int j = 0; j < envelopeBody.GetLength(1); j++)
                    {
                        int minEntity = envelopeBody.GetLength(2);
                        int maxEntity = 0;
                        for (int k = 0; k < envelopeBody.GetLength(2); k++)
                        {
                            if (envelopeBody[i, j, k])
                            {
                                if (k < minEntity) minEntity = k;
                                if (k > maxEntity) maxEntity = k;
                            }
                        }
                        for (int k = minEntity; k < maxEntity; k++)
                        {
                            envelopeBody[i, j, k] = true;
                        }
                    }
                }

                //xzy
                for (int i = 0; i < envelopeBody.GetLength(0); i++)
                {
                    for (int k = 0; k < envelopeBody.GetLength(2); k++)
                    {
                        int minEntity = envelopeBody.GetLength(1);
                        int maxEntity = 0;
                        for (int j = 0; j < envelopeBody.GetLength(1); j++)
                        {
                            if (envelopeBody[i, j, k])
                            {
                                if (j < minEntity) minEntity = j;
                                if (j > maxEntity) maxEntity = j;
                            }
                        }
                        for (int j = minEntity; j < maxEntity; j++)
                        {
                            envelopeBody[i, j, k] = true;
                        }
                    }
                }

                //yzx
                for (int j = 0; j < envelopeBody.GetLength(1); j++)
                {
                    for (int k = 0; k < envelopeBody.GetLength(2); k++)
                    {
                        int minEntity = envelopeBody.GetLength(0);
                        int maxEntity = 0;
                        for (int i = 0; i < envelopeBody.GetLength(0); i++)
                        {
                            if (envelopeBody[i, j, k])
                            {
                                if (i < minEntity) minEntity = i;
                                if (i > maxEntity) maxEntity = i;
                            }
                        }
                        for (int i = minEntity; i < maxEntity; i++)
                        {
                            envelopeBody[i, j, k] = true;
                        }
                    }
                }
            }

            //make dictionary containing ion type with the count
            Dictionary<byte, int> currTypeCount = new();
            int envelopeIonCount = 0;
            foreach (Vector3 ion in ionToIonTypeDict.Keys)
            {
                //if the ion is in the box defined by min and max
                if (ion.X >= min.X && ion.X <= max.X && ion.Y >= min.Y && ion.Y <= max.Y && ion.Z >= min.Z && ion.Z <= max.Z)
                {
                    int xCoord = (int)((ion.X - min.X) / gridSize);
                    int yCoord = (int)((ion.Y - min.Y) / gridSize);
                    int zCoord = (int)((ion.Z - min.Z) / gridSize);
                    //if this ion falls within the bounds of the envlope as created by the cluster
                    if (envelopeBody[xCoord, yCoord, zCoord])
                    {
                        if (!currTypeCount.ContainsKey(ionToIonTypeDict[ion]))
                            currTypeCount.Add(ionToIonTypeDict[ion], 1);
                        else
                            currTypeCount[ionToIonTypeDict[ion]]++;

                        envelopeIonCount++;

                        thisEnvelopeList.Add(ion);
                    }
                }
            }

            envelopeIonList.Add(thisEnvelopeList);

            int gridElementCount = 0;
            foreach (bool isFilledIn in envelopeBody)
            {
                if (isFilledIn)
                    gridElementCount++;
            }

            sb.AppendLine($"Total atoms in cluster = {envelopeIonCount}");
            sb.AppendLine($"Grid elements={gridElementCount}, ions per grid element={((float)envelopeIonCount / gridElementCount).ToString($"f{ROUNDING_LENGTH}")}");
            for (byte i = 0; i < typeToNameDict.Count; i++)
            {
                double percent = 0;
                double error = 0;
                //copies other script in that it just doesn't display the other ion types if there are 0 of them in the envelope
                if (currTypeCount.ContainsKey(i))
                {
                    percent = ((float)currTypeCount[i] / envelopeIonCount);
                    error = Math.Sqrt(percent * (1.0 - percent) / envelopeIonCount);
                    string percentStr = percent.ToString($"p{ROUNDING_LENGTH}");
                    string errorStr = error.ToString($"p{ROUNDING_LENGTH + 1}");
                    sb.AppendLine($"{typeToNameDict[i]} ions = {currTypeCount[i]}, concentration = {percentStr} +/- {errorStr}");
                    gyrationRowList.AddRange(new Object[] { currTypeCount[i], percentStr, errorStr });
                }
                else
                {
                    //this means theres 0 for that one
                    gyrationRowList.AddRange(new Object[] { 0, percent.ToString($"p{ROUNDING_LENGTH}"), error.ToString($"p{ROUNDING_LENGTH}") });
                }
            }
            sb.AppendLine("\n");
            gyrationRowList.AddRange(new Object[] { envelopeIonCount, ((float)envelopeIonCount / gridElementCount).ToString($"f{ROUNDING_LENGTH}") });

            gyrationTable.Rows.Add(gyrationRowList.ToArray());

            toRet.Add(currTypeCount);
            envelopeBodyList.Add(envelopeBody);
        }

        //viewBuilder.AddTable("Pre-Envelope Cluster Information", gyrationRows);

        return (toRet, sb.ToString(), envelopeIonList, envelopeBodyList);
    }

    /// <summary>
    /// Helper function to parse the user input of ranges, turning it from a string into a list of integers that corresponds to the ranges specified.
    /// </summary>
    /// <param name="rangeStr">string formatted like "2 5 3" where 2,5, and 3 are all ranges in the user specified ranges</param>
    /// <returns>returns a boolean for good user input and a list of the ranges</returns>
    private (bool, List<byte>) ParseRangeInput(string rangeStr, IMassSpectrumRangeManagerProvider massSpectrumRangeManagerProvider)
    {
        var massSpectrumRangeManager = massSpectrumRangeManagerProvider.Resolve(ID)!;
        var ionInfoRangeDict = massSpectrumRangeManager.GetRanges();
        var rawRanges = rangeStr.Split(" ");
        List<byte> toRet = new();

        var rangeList = ionInfoRangeDict.Values.ToList();

        for (int i = 0; i < rawRanges.Length; i++)
        {
            int currRangeInt;
            try
            {
                currRangeInt = Int32.Parse(rawRanges[i]);
            }
            catch
            {
                return (false, new List<byte>());
            }
            if (currRangeInt > rangeList.Count || currRangeInt < 1)
                return (false, new List<byte>());
            toRet.Add((byte)(currRangeInt - 1));
        }
        return (true, toRet);
    }

    /// <summary>
    /// Adds a table view of the composition data, along with returning a string of the same data.
    /// </summary>
    /// <param name="ionData">IIonData object used to examine ion data</param>
    /// <param name="viewBuilder">IViewBuilder object to construct the table and add to it</param>
    /// <returns>A string of the formatted composition data</returns>
    private static string CalculateComposition(IIonData ionData, IViewBuilder viewBuilder)
    {
        StringBuilder sb = new();
        ulong totalIons = 0;
        foreach (ulong thisIonCount in ionData.GetIonTypeCounts().Values)
        {
            totalIons += thisIonCount;
        }
        List<CompositionRow> compositionRows = new();
        foreach (KeyValuePair<IIonTypeInfo, ulong> basicIonDict in ionData.GetIonTypeCounts())
        {
            double percent = (double)basicIonDict.Value / totalIons;
            double error = Math.Sqrt(percent * (1.0 - percent) / totalIons);
            string errorStr = error.ToString($"p{ROUNDING_LENGTH + 1}");
            string percentStr = percent.ToString($"p{ROUNDING_LENGTH}");
            sb.AppendLine($"{basicIonDict.Key.Name} ions: {basicIonDict.Value}, composition: {percentStr} +/- {errorStr}");
            compositionRows.Add(new CompositionRow(basicIonDict.Key.Name, basicIonDict.Value, percentStr, errorStr));
        }
        compositionRows.Add(new CompositionRow("Total", totalIons, "", ""));
        sb.AppendLine($"Total Ions: {totalIons}");

        viewBuilder.AddTable("Overall Composition", compositionRows);
        return sb.ToString();
    }

    /// <summary>
    /// Retrieves the Min and Max points of this data sample, returning a fomratted string as well as displaying it in a table
    /// in the given ViewBuilder
    /// </summary>
    /// <param name="ionData">IIonData object used to examine ion data</param>
    /// <param name="viewBuilder">IViewBuilder object to construct the table and add to it</param>
    /// <returns>A formatted string that includes the Min and Max point extents</returns>
    private static string? GetLimits(IIonData ionData, IViewBuilder viewBuilder, float atomSeparation)
    {
        StringBuilder sb = new();
        List<LimitsRow> limitsRows = new();

        var max = ionData.Extents.Max;
        var min = ionData.Extents.Min;
        var diff = max - min;

        int numX = (int)((max.X - min.X) / atomSeparation) + 1;
        int numY = (int)((max.Y - min.Y) / atomSeparation) + 1;
        int numZ = (int)((max.Z - min.Z) / atomSeparation) + 1;

        if(numX * numY * numZ > 120_000_000)
        {
            MessageBox.Show("Atom separation too fine. Choose a bigger voxel size. ");
            return null;
        }

        string minX = min.X.ToString($"f{ROUNDING_LENGTH}");
        string maxX = max.X.ToString($"f{ROUNDING_LENGTH}");
        string minY = min.Y.ToString($"f{ROUNDING_LENGTH}");
        string maxY = max.Y.ToString($"f{ROUNDING_LENGTH}");
        string minZ = min.Z.ToString($"f{ROUNDING_LENGTH}");
        string maxZ = max.Z.ToString($"f{ROUNDING_LENGTH}");

        string xDiff = diff.X.ToString($"f{ROUNDING_LENGTH}");
        string yDiff = diff.Y.ToString($"f{ROUNDING_LENGTH}");
        string zDiff = diff.Z.ToString($"f{ROUNDING_LENGTH}");

        limitsRows.Add(new LimitsRow("X", minX, maxX, xDiff));
        sb.AppendLine($"X limits {minX} to {maxX} [{xDiff}] nm");

        limitsRows.Add(new LimitsRow("Y", minY, maxY, yDiff));
        sb.AppendLine($"Y limits {minY} to {maxY} [{yDiff}] nm");

        limitsRows.Add(new LimitsRow("Z", minZ, maxZ, zDiff));
        sb.AppendLine($"Z limits {minZ} to {maxZ} [{zDiff}] nm");

        viewBuilder.AddTable("Limits", limitsRows);
        return sb.ToString();
    }

    /// <summary>
    /// Returns a List of position data corresponding to the ions selected by the user, along with a
    /// list that has the indexes of each point stored as well (essentially a list holding its own index number to begin)
    /// </summary>
    /// <param name="ionData">IIonData object used to examine ion data</param>
    /// <param name="ranges">A list of integers corresponding to the indexes of the selected ranges, as given by ParseRangeInput</param>
    /// <returns>A list of Vectors corresponding to Ion position data, and a List of ints corresponding to the indexes of the selected Ions</returns>
    private static (List<Vector3>, List<int>, Dictionary<Vector3, byte>, string, int) GetSelectedIons(IIonData ionData, List<byte> ranges)
    {
        List<Vector3> selectedIons = new();
        List<int> ionIndexList = new();
        Dictionary<Vector3, byte> ionToIonTypeDict = new();
        StringBuilder sb = new();
        int index = 0;
        int unrangedIons = 0;

        string[] requiredSections = new string[] { IonDataSectionName.Position, IonDataSectionName.IonType };
        foreach (var chunk in ionData.CreateSectionDataEnumerable(requiredSections))
        {
            var positions = chunk.ReadSectionData<Vector3>(IonDataSectionName.Position).Span;
            var ionTypes = chunk.ReadSectionData<byte>(IonDataSectionName.IonType).Span;

            //go through every ion
            for (int i = 0; i < positions.Length; i++)
            {
                if (ranges.Contains(ionTypes[i]))
                {
                    //Ion ionToAdd = new(positions[i], numToNameDict[ionTypes[i]], ionTypes[i]);
                    selectedIons.Add(positions[i]);
                    ionIndexList.Add(index++);
                }

                //all ranged ions into the dictionary
                if (ionTypes[i] != 255)
                {
                    if (!ionToIonTypeDict.ContainsKey(positions[i]))
                        ionToIonTypeDict.Add(positions[i], ionTypes[i]);
                    else
                    {
                        //I'm hoping this would never happen
                    }
                }
                else
                    unrangedIons++;
            }
        }

        sb.AppendLine($"{index} atoms in selected ranges found in {ionData.IonCount} atoms");

        return (selectedIons, ionIndexList, ionToIonTypeDict, sb.ToString(), unrangedIons);
    }

    /// <summary>
    /// Function to get a dictionary of the name of an ion given the "index" of said ion
    /// </summary>
    /// <param name="ionData">IIonData object used to examine ion data</param>
    /// <returns></returns>
    private static Dictionary<byte, string> GetNumToNameDict(IIonData ionData)
    {
        Dictionary<byte, string> toRet = new();
        //THIS ASSUMES IT COMES BACK IN A DETERMINISTIC ORDER
        var ionList = ionData.Ions.ToList();
        for (byte i = 0; i < ionList.Count; i++)
        {
            toRet.Add(i, ionList[i].Name);
        }

        return toRet;
    }

    /// <summary>
    /// Creates the cluster list given the ions wanted and an adjacency list (a graph representation)
    /// It is a list of lists of ints. The outermost list is the list of clusters, where each list inside that
    /// is a list of indexes of the ions in that cluster
    /// </summary>
    /// <param name="ionIndexList">A list of ints that correspond to the indexes of the ions selected</param>
    /// <param name="adjacencyList">An adjacency list corresponding to the edges</param>
    /// <returns></returns>
    static List<List<int>> GraphToClusters(List<int> ionIndexList, List<int>[] adjacencyList)
    {
        List<List<int>> clusterList = new();
        bool[] visitedNodes = new bool[ionIndexList.Count];

        while (ionIndexList.Count > 0)
        {
            clusterList.Add(BFSSearch(adjacencyList, ionIndexList[0], ionIndexList, visitedNodes));
        }

        return clusterList;
    }

    /// <summary>
    /// Runs a Breadth First Search on a graph
    /// </summary>
    /// <param name="adjacencyList">The adjacency list of the graph (the graph structure)</param>
    /// <param name="ionToSearchIndex">The initial ion's index to start the search from</param>
    /// <param name="ionIndexList">List of all ions being searched on (their indexes)</param>
    /// <param name="visitedNodes">Boolean array keeping track of which ions have been visited</param>
    /// <returns>A list of integers representing the cluster, where the ints are the indexes of the ions</returns>
    static List<int> BFSSearch(List<int>[] adjacencyList, int ionToSearchIndex, List<int> ionIndexList, bool[] visitedNodes)
    {
        List<int> clusterList = new();
        Queue<int> queue = new();
        queue.Enqueue(ionToSearchIndex);

        while (queue.Count > 0)
        {
            int currIon = queue.Dequeue();
            if (visitedNodes[currIon]) continue;

            visitedNodes[currIon] = true;
            clusterList.Add(currIon);
            ionIndexList.Remove(currIon);
            foreach(int nextIon in adjacencyList[currIon])
            {
                if (visitedNodes[nextIon]) continue;
                queue.Enqueue(nextIon);
            }
        }


        return clusterList;
    }

    /// <summary>
    /// Creates the graph structure given a list of ions selected and a max separation value
    /// </summary>
    /// <param name="ionList">List of Ions to create a graph from</param>
    /// <param name="maxSeparation">The maximum distance between particles to be considered in the same cluster</param>
    /// <returns>Returns an adjacency list which represents the structure of the graph</returns>
    static List<int>[] CreateAdjacencyList(List<Vector3> ionList, float maxSeparation, IIonData ionData)
    {
        float maxSeparationSquared = maxSeparation * maxSeparation;

        //the outermost list is indexed by what number vertex it is
        //the innermost list is just a list of the indexes that can be reached from this "ion"
        List<int>[] adjacencyList = new List<int>[ionList.Count];

        Vector3 min = ionData.Extents.Min;
        Vector3 max = ionData.Extents.Max;
        int numX = (int)((max.X - min.X) / maxSeparation) + 1;
        int numY = (int)((max.Y - min.Y) / maxSeparation) + 1;
        int numZ = (int)((max.Z - min.Z) / maxSeparation) + 1;
        List<(Vector3, int)>[,,] ionGrid = new List<(Vector3, int)>[numX, numY, numZ];

        for (int i = 0; i < ionList.Count; i++)
        {
            var ion = ionList[i];
            int indexX = (int)((ion.X - min.X) / maxSeparation);
            int indexY = (int)((ion.Y - min.Y) / maxSeparation);
            int indexZ = (int)((ion.Z - min.Z) / maxSeparation);
            if (ionGrid[indexX, indexY, indexZ] == null)
                ionGrid[indexX, indexY, indexZ] = new List<(Vector3, int)>();
            ionGrid[indexX, indexY, indexZ].Add((ion, i));
        }

        //go through every box
        for (int i = 0; i < numX; i++)
        {
            for (int j = 0; j < numY; j++)
            {
                for (int k = 0; k < numZ; k++)
                {
                    //first make sure there are items here
                    if (ionGrid[i, j, k] == null) continue;

                    //for each box, check every possible relative box
                    for (int relativeRow = -1; relativeRow < 2; relativeRow++)
                    {
                        int currRow = i + relativeRow;
                        //check for bounds
                        if (currRow < 0 || currRow >= numX)
                            continue;

                        for (int relativeColumn = -1; relativeColumn < 2; relativeColumn++)
                        {
                            int currColumn = j + relativeColumn;
                            //check for bounds
                            if (currColumn < 0 || currColumn >= numY)
                                continue;

                            for (int relativePage = -1; relativePage < 2; relativePage++)
                            {
                                int currPage = k + relativePage;
                                //check for bounds
                                if (currPage < 0 || currPage >= numZ)
                                    continue;

                                //check to see if this contains any points
                                if (ionGrid[currRow, currColumn, currPage] == null) continue;

                                //if got to here, we have a valid bound. Now compare all items in each list
                                foreach ((Vector3 ion1, int index1) in ionGrid[i, j, k])
                                {
                                    if (adjacencyList[index1] == null)
                                        adjacencyList[index1] = new();

                                    foreach ((Vector3 ion2, int index2) in ionGrid[currRow, currColumn, currPage])
                                    {
                                        if (Vector3.DistanceSquared(ion1, ion2) <= maxSeparationSquared)
                                        {
                                            if (adjacencyList[index2] == null)
                                                adjacencyList[index2] = new();

                                            adjacencyList[index1].Add(index2);
                                            adjacencyList[index2].Add(index1);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        return adjacencyList;
    }

    /// <summary>
    /// Method to remove clusters that do not meet the user-specified criteria for size of cluster.
    /// </summary>
    /// <param name="clusterList">A list of lists of integers, where each list of integer is a cluster, and each int corresponds to the index of that ion in a main list of ions</param>
    /// <param name="minIonsPerCluster">An integer value specifying the minimum amount of ions to make a cluster big enough to be considered</param>
    /// <returns>A formatted string for outputting information on the clusters being removed (size, counts)</returns>
    static string RemoveSmallClusters(List<List<int>> clusterList, int minIonsPerCluster)
    {
        StringBuilder sb = new();
        Dictionary<int, int> clusterSizeCountDict = new();
        int clustersRemoved = 0;
        List<List<int>> clustersToRemove = new();

        foreach (List<int> cluster in clusterList)
        {
            if (cluster.Count < minIonsPerCluster)
            {
                //save information on size of cluster being deleted
                if (!clusterSizeCountDict.ContainsKey(cluster.Count))
                    clusterSizeCountDict.Add(cluster.Count, 1);
                else
                    clusterSizeCountDict[cluster.Count]++;
                clustersRemoved++;
                clustersToRemove.Add(cluster);
            }
        }
        foreach (List<int> cluster in clustersToRemove)
        {
            clusterList.Remove(cluster);
        }

        //create the formatted string for outputting to user
        for (int size = 1; size < minIonsPerCluster; size++)
        {
            int count;
            if (clusterSizeCountDict.ContainsKey(size))
                count = clusterSizeCountDict[size];
            else
                count = 0;

            sb.AppendLine($"{count}\t\t{size} atom clusters");
        }
        sb.AppendLine($"{clustersRemoved} clusters containing 1 to {minIonsPerCluster - 1} atoms not included");

        return sb.ToString();
    }
}

public class CompositionRow
{
    public string Name { get; set; }
    public ulong Count { get; set; }
    public string Composition { get; set; }
    public string Error { get; set; }

    public CompositionRow(string name, ulong count, string composition, string error)
    {
        Name = name;
        Count = count;
        Composition = composition;
        Error = error;
    }
}

public class LimitsRow
{
    public string Axis { get; set; }
    public string Min { get; set; }
    public string Max { get; set; }
    public string Length { get; set; }

    public LimitsRow(string axis, string min, string max, string length)
    {
        Axis = axis;
        Min = min;
        Max = max;
        Length = length;
    }
}