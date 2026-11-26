using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceJourney;

// Imported stock meshes usually have no CPU copy. Read the original buffers without modifying them.
internal sealed class PortalMeshReadback : IDisposable
{
    private const int BufferBudget = 24;
    internal const int MeshByteBudget = 2 * 1024 * 1024;
    private static int inFlight;
    private readonly byte[][] buffers;
    private readonly VertexAttributeDescriptor[] layout;
    private readonly SubMeshDescriptor[] subMeshes;
    private readonly Bounds bounds;
    private readonly IndexFormat indexFormat;
    private readonly int vertexCount;
    private int remaining;
    private bool failed, disposed;

    internal bool Ready => remaining == 0;
    internal int ByteCount { get; }

    internal static bool CanStart(Mesh mesh) =>
        inFlight + mesh.vertexBufferCount + 1 <= BufferBudget;

    internal static PortalMeshReadback Start(Mesh mesh)
    {
        if (!SystemInfo.supportsAsyncGPUReadback || mesh.vertexBufferCount == 0 ||
            !PortalMeshSlice.SupportsShape(mesh))
            return null;

        var gpu = new GraphicsBuffer[mesh.vertexBufferCount + 1];
        PortalMeshReadback readback = null;
        try
        {
            for (int stream = 0; stream < gpu.Length - 1; stream++)
                gpu[stream] = mesh.GetVertexBuffer(stream);
            gpu[^1] = mesh.GetIndexBuffer();
            long size = 0;
            foreach (var source in gpu)
            {
                if (source == null || !source.IsValid())
                    return null;
                size += (long)source.count * source.stride;
            }
            if (size > MeshByteBudget)
                return null;

            readback = new PortalMeshReadback(mesh, (int)size);
            for (int index = 0; index < gpu.Length; index++)
            {
                readback.Request(gpu[index], index);
                gpu[index] = null; // completion owns the handle
            }
            return readback;
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or UnityException)
        {
            readback?.Dispose();
            return null;
        }
        finally
        {
            foreach (var source in gpu)
                source?.Dispose();
        }
    }

    private PortalMeshReadback(Mesh mesh, int byteCount)
    {
        ByteCount = byteCount;
        buffers = new byte[mesh.vertexBufferCount + 1][];
        layout = mesh.GetVertexAttributes();
        subMeshes = new SubMeshDescriptor[mesh.subMeshCount];
        for (int index = 0; index < subMeshes.Length; index++)
            subMeshes[index] = mesh.GetSubMesh(index);
        bounds = mesh.bounds;
        indexFormat = mesh.indexFormat;
        vertexCount = mesh.vertexCount;
    }

    private void Request(GraphicsBuffer source, int index)
    {
        inFlight++;
        remaining++;
        try
        {
            AsyncGPUReadback.Request(source, request =>
            {
                try
                {
                    if (request.hasError) failed = true;
                    if (!failed && !disposed)
                        buffers[index] = request.GetData<byte>().ToArray();
                }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException or UnityException)
                {
                    failed = true;
                }
                finally
                {
                    source.Dispose();
                    remaining--;
                    inFlight--;
                }
            });
        }
        catch
        {
            remaining--;
            inFlight--;
            throw;
        }
    }

    internal PortalMeshSlice CreateSlice()
    {
        if (disposed || failed || !Ready)
            return null;

        var allocation = Mesh.AllocateWritableMeshData(1);
        var data = allocation[0];
        Mesh copy = null;
        try
        {
            data.SetVertexBufferParams(vertexCount, layout);
            for (int stream = 0; stream < buffers.Length - 1; stream++)
            {
                var target = data.GetVertexData<byte>(stream);
                if (target.Length != buffers[stream].Length)
                    return null;
                target.CopyFrom(buffers[stream]);
            }
            int stride = indexFormat == IndexFormat.UInt32 ? 4 : 2;
            if (buffers[^1].Length % stride != 0)
                return null;
            int count = buffers[^1].Length / stride;
            data.SetIndexBufferParams(count, indexFormat);
            data.GetIndexData<byte>().CopyFrom(buffers[^1]);
            data.subMeshCount = subMeshes.Length;
            for (int index = 0; index < subMeshes.Length; index++)
                data.SetSubMesh(index, subMeshes[index], MeshUpdateFlags.DontRecalculateBounds);

            copy = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            Mesh.ApplyAndDisposeWritableMeshData(allocation, copy, MeshUpdateFlags.DontRecalculateBounds);
            allocation = default;
            copy.bounds = bounds;
            return PortalMeshSlice.Create(copy);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or UnityException)
        {
            return null;
        }
        finally
        {
            if (allocation.Length != 0)
                allocation.Dispose();
            if (copy) UnityEngine.Object.Destroy(copy);
        }
    }

    public void Dispose()
    {
        disposed = true;
        Array.Clear(buffers, 0, buffers.Length);
    }
}
