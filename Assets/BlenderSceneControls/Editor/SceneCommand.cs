namespace BlenderSceneControls
{
    internal enum SceneCommand
    {
        Grab, Rotate, Scale, Duplicate, AddPrimitive, FrameSelected,
        ToggleMeshMode, ObjectMode, MeshMode, Vertex, Edge, Face, XRay, Median, ActiveElement,
        SelectAll, DeselectAll, BoxSelect, SelectLoop,
        RecoverConnectivity, TrisToQuads, ConfirmPreparation, CancelPreparation,
        DeleteDissolve, DeleteVertices, DeleteEdges, DeleteFaces, DeleteOnlyEdgesFaces, DeleteOnlyFaces,
        DissolveVertices, DissolveEdges, DissolveFaces,
        Extrude, LoopCut, Inset, Bevel, VertexBevel, ApplyScaleMenu, ApplyScale, SaveMesh, SaveMeshAs, ExportFbx
    }
}
