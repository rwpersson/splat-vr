using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Minimal reader for 3DGS .ply files (binary little-endian): positions and opacities only.
/// Opacity in 3DGS files is stored before the sigmoid (a "logit"), so it's converted here before comparing.
/// </summary>
public static class SplatPly
{
    public static bool TryReadSolidPositions(string path, float minOpacity, out Vector3[] positions, out string error)
    {
        positions = null;
        if (!TryRead(path, out Vector3[] all, out float[] opacities, out error))
            return false;
        var solid = new List<Vector3>(all.Length / 2);
        for (int i = 0; i < all.Length; i++)
        {
            if (opacities[i] >= minOpacity)
                solid.Add(all[i]);
        }
        positions = solid.ToArray();
        return true;
    }

    /// <summary>Every splat's position and opacity (0–1, after the sigmoid).</summary>
    public static bool TryRead(string path, out Vector3[] positions, out float[] opacities, out string error)
    {
        positions = null;
        opacities = null;
        if (!path.EndsWith(".ply", StringComparison.OrdinalIgnoreCase))
        {
            error = "only .ply is supported for now (convert other formats with splat-transform first)";
            return false;
        }

        byte[] data = File.ReadAllBytes(path);
        int headerEnd = IndexOf(data, Encoding.ASCII.GetBytes("end_header\n"));
        if (headerEnd < 0)
        {
            error = "no PLY header";
            return false;
        }
        string[] header = Encoding.ASCII.GetString(data, 0, headerEnd).Split('\n');
        if (Array.IndexOf(header, "format binary_little_endian 1.0") < 0)
        {
            error = "only binary little-endian PLY is supported";
            return false;
        }

        // Byte offset of each property within one vertex record.
        int count = 0, stride = 0;
        var offsets = new Dictionary<string, int>();
        var types = new Dictionary<string, string>();
        bool inVertex = false;
        foreach (string line in header)
        {
            string[] parts = line.Trim().Split(' ');
            if (parts[0] == "element")
            {
                inVertex = parts[1] == "vertex";
                if (inVertex)
                    count = int.Parse(parts[2]);
            }
            else if (parts[0] == "property" && inVertex)
            {
                offsets[parts[2]] = stride;
                types[parts[2]] = parts[1];
                stride += SizeOf(parts[1]);
            }
        }
        foreach (string name in new[] { "x", "y", "z", "opacity" })
        {
            if (!types.TryGetValue(name, out string type) || (type != "float" && type != "float32"))
            {
                error = $"property '{name}' missing or not a float";
                return false;
            }
        }

        positions = new Vector3[count];
        opacities = new float[count];
        int start = headerEnd + "end_header\n".Length;
        for (int i = 0; i < count; i++)
        {
            int record = start + i * stride;
            positions[i] = new Vector3(
                BitConverter.ToSingle(data, record + offsets["x"]),
                BitConverter.ToSingle(data, record + offsets["y"]),
                BitConverter.ToSingle(data, record + offsets["z"]));
            float logit = BitConverter.ToSingle(data, record + offsets["opacity"]);
            opacities[i] = 1f / (1f + Mathf.Exp(-logit));
        }

        error = null;
        return true;
    }

    private static int SizeOf(string type) => type switch
    {
        "char" or "uchar" or "int8" or "uint8" => 1,
        "short" or "ushort" or "int16" or "uint16" => 2,
        "int" or "uint" or "float" or "int32" or "uint32" or "float32" => 4,
        "double" or "float64" => 8,
        _ => throw new IOException($"unknown PLY property type {type}"),
    };

    private static int IndexOf(byte[] data, byte[] pattern)
    {
        int limit = Math.Min(data.Length - pattern.Length, 64 * 1024); // the header is small
        for (int i = 0; i <= limit; i++)
        {
            int j = 0;
            while (j < pattern.Length && data[i + j] == pattern[j])
                j++;
            if (j == pattern.Length)
                return i;
        }
        return -1;
    }
}
