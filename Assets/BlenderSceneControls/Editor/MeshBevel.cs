using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BlenderSceneControls
{
    // Local face trimming, shared unselected rails and oriented corner-hole closure.
    // No positional welding: only points belonging to the same original rail may share IDs.
    internal static class MeshBevel
    {
        internal const int MaxSegments = 16;
        private static (int, int) Key(int a, int b) => MeshInset.Key(a, b);
        internal static MeshDocument Apply(MeshDocument source, float width, int segments, bool vertexOnly)
        {
            source.Validate();
            if (!float.IsFinite(width) || width < 0 || segments < 1 || segments > MaxSegments)
                throw new InvalidOperationException("Bevel needs finite nonnegative width and 1–16 segments.");
            Budget(source, 0, 0);
            var selection = vertexOnly ? source.SelectedVertices : source.SelectedEdges;
            if (selection.Length == 0) throw new InvalidOperationException(vertexOnly ? "Select vertices for Vertex Bevel." : "Select edges for Edge Bevel.");
            if (width == 0) return source.Clone();
            var selected = selection.ToHashSet();
            var pos = source.Vertices.ToDictionary(v => v.Id, v => v.Position);
            var adjacency = source.EdgeFaces();
            var byPair = source.Edges.ToDictionary(e => Key(e.A, e.B));
            var affected = vertexOnly ? selected : source.Edges.Where(e => selected.Contains(e.Id)).SelectMany(e => new[] { e.A, e.B }).ToHashSet();
            var incident = affected.ToDictionary(id => id, id => source.Edges.Where(e => e.A == id || e.B == id).ToArray());
            foreach (var id in affected)
            {
                if (incident[id].Any(e => adjacency[e.Id].Length == 0 || adjacency[e.Id].Length > 2))
                    throw new InvalidOperationException("Bevel requires a surface fan without wires or non-manifold edges.");
                var faces = source.Faces.Where(f => f.Corners.Any(c => c.Vertex == id)).ToArray();
                var boundary = incident[id].Count(e => adjacency[e.Id].Length == 1);
                if (boundary != 0 && boundary != 2 || faces.Length != incident[id].Length - (boundary == 2 ? 1 : 0))
                    throw new InvalidOperationException("Bevel vertex fan branches or contains disconnected shells.");
                if (!vertexOnly && incident[id].Count(e => selected.Contains(e.Id) && adjacency[e.Id].Length == 1) > 1)
                    throw new InvalidOperationException("Adjacent selected boundary rims are unavailable; bevel one rim or use Vertex Bevel.");
                foreach (var e in incident[id].Where(e => adjacency[e.Id].Length == 2))
                {
                    var pair = source.Faces.Where(f => adjacency[e.Id].Contains(f.Id)).ToArray();
                    bool Forward(MeshFace f) => Enumerable.Range(0, f.Corners.Length).Any(i => f.Corners[i].Vertex == e.A && f.Corners[(i+1)%f.Corners.Length].Vertex == e.B);
                    if (Forward(pair[0]) == Forward(pair[1])) throw new InvalidOperationException("Bevel requires consistent face winding.");
                }
            }
            // Conservative before-allocation bound, including corner boundaries.
            var degree = incident.Values.Sum(e => (long)e.Length);
            Budget(source, vertexOnly ? affected.Count * (long)segments * segments : selected.Count * (long)segments + affected.Count,
                vertexOnly ? affected.Count * 3L * segments * segments + degree * segments : selected.Count * 4L * segments + degree * (segments + 2L));
            var result = source.Clone();
            var origin = source.Vertices.ToDictionary(v => v.Id, v => v.Id);
            var rails = new Dictionary<(int vertex, int edge), int>();
            var points = new Dictionary<(int face, int vertex), int>();
            var newFaces = new HashSet<int>();
            var changed = new HashSet<int>();
            int Add(Vector3 p, int old)
            {
                if (!GrabMath.Finite(p)) throw new InvalidOperationException("Non-finite bevel point.");
                var id = result.NextId++; result.Vertices.Add(new MeshVertex { Id = id, Position = p }); pos[id] = p; origin[id] = old; return id;
            }
            int Rail(int old, MeshEdge e, Vector3 p)
            {
                var other = e.A == old ? e.B : e.A;
                var delta = p-pos[old]; var direction = pos[other]-pos[old];
                var t = Vector3.Dot(delta, direction)/direction.sqrMagnitude;
                if (t <= 0 || t >= 1 || Vector3.Cross(delta, direction).magnitude > 1e-5f * direction.magnitude)
                    throw new InvalidOperationException("Bevel width crosses a rail. Reduce width.");
                var key = (old, e.Id);
                if (rails.TryGetValue(key, out var existing))
                {
                    if ((pos[existing]-p).magnitude > Mathf.Max(1e-6f, width*1e-4f))
                        throw new InvalidOperationException("Unequal corner offsets on a shared rail are unavailable.");
                    return existing;
                }
                return rails[key] = Add(p, old);
            }
            Vector3 Normal(MeshFace f)
            {
                var p = f.Corners.Select(c => pos[c.Vertex]).ToArray();
                MeshTriangulation.Triangulate(p);
                return Enumerable.Range(0,p.Length).Aggregate(Vector3.zero, (sum,i) => sum+Vector3.Cross(p[i]-p[0],p[(i+1)%p.Length]-p[0])).normalized;
            }
            if (vertexOnly)
            {
                foreach (var id in affected.OrderBy(i=>i))
                foreach (var edge in incident[id])
                {
                    var other = edge.A == id ? edge.B : edge.A;
                    Rail(id, edge, pos[id]+(pos[other]-pos[id]).normalized*width);
                }
            }
            else
            {
                foreach (var f in source.Faces.Where(f=>f.Corners.Any(c=>affected.Contains(c.Vertex))))
                {
                    var n = Normal(f);
                    for (var i=0;i<f.Corners.Length;i++)
                    {
                        var id = f.Corners[i].Vertex;
                        if (!affected.Contains(id)) continue;
                        var prev = f.Corners[(i+f.Corners.Length-1)%f.Corners.Length].Vertex;
                        var next = f.Corners[(i+1)%f.Corners.Length].Vertex;
                        var a = byPair[Key(prev,id)]; var b = byPair[Key(id,next)];
                        bool sa = selected.Contains(a.Id), sb = selected.Contains(b.Id);
                        if (!sa && !sb) continue;
                        var inwardA = Vector3.Cross(n,(pos[id]-pos[prev]).normalized);
                        var inwardB = Vector3.Cross(n,(pos[next]-pos[id]).normalized);
                        var dot = Vector3.Dot(inwardA,inwardB); var det = 1-dot*dot;
                        if (det < 1e-8f) throw new InvalidOperationException("Bevel corner is collinear or too sharp.");
                        var wa = sa ? width : 0; var wb = sb ? width : 0;
                        var p = pos[id]+inwardA*((wa-dot*wb)/det)+inwardB*((wb-dot*wa)/det);
                        if (Vector3.Dot(Vector3.Cross(pos[id]-pos[prev],pos[next]-pos[id]),n) <= 0)
                            throw new InvalidOperationException("Concave selected edge corners are unavailable.");
                        points[(f.Id,id)] = sa && sb ? Add(p,id) : Rail(id,sa ? b : a,p);
                    }
                }
            }
            // Rail cuts from opposite ends must never pass each other.
            foreach (var edge in source.Edges)
                if (rails.TryGetValue((edge.A,edge.Id),out var a) && rails.TryGetValue((edge.B,edge.Id),out var b) &&
                    Vector3.Dot(pos[b]-pos[a],pos[edge.B]-pos[edge.A]) <= 1e-10f)
                    throw new InvalidOperationException("Bevel widths overlap on an edge. Reduce width (overlap clamp unavailable).");

            var faceRows = new Dictionary<(int face,int vertex), MeshCorner[]>();
            foreach (var f in source.Faces)
            {
                // Untouched surfaces include valid tagged zero-move extrusion walls.
                // They must not be sent through the planar bevel offset solver.
                if (!f.Corners.Any(c => affected.Contains(c.Vertex))) continue;
                var row = new List<MeshCorner>();
                for (var i=0;i<f.Corners.Length;i++)
                {
                    var c=f.Corners[i]; var old=c.Vertex;
                    var prev=f.Corners[(i+f.Corners.Length-1)%f.Corners.Length]; var next=f.Corners[(i+1)%f.Corners.Length];
                    var local = new List<MeshCorner>();
                    MeshCorner Corner(int id, MeshCorner neighbor)
                    {
                        if (id==old) return MeshExtrusion.Copy(c,id);
                        var d=pos[neighbor.Vertex]-pos[old]; var t=Vector3.Dot(pos[id]-pos[old],d)/d.sqrMagnitude;
                        // Interpolate only on a real original face rail. Miter points keep
                        // their authored corner island; no cross-face averaging of seams.
                        return Vector3.Cross(pos[id]-pos[old],d).magnitude < 1e-6f*d.magnitude ?
                            MeshLoopCut.Interpolate(c,neighbor,id,t) : MeshExtrusion.Copy(c,id);
                    }
                    if (affected.Contains(old))
                    {
                        if (vertexOnly && Vector3.Dot(Vector3.Cross(pos[old]-pos[prev.Vertex], pos[next.Vertex]-pos[old]), Normal(f)) <= 0)
                            throw new InvalidOperationException("Concave Vertex Bevel corners are unavailable.");
                        if (!vertexOnly && points.TryGetValue((f.Id,old),out var one)) local.Add(Corner(one, selected.Contains(byPair[Key(prev.Vertex,old)].Id) ? next : prev));
                        else
                        {
                            var incoming=rails.TryGetValue((old,byPair[Key(prev.Vertex,old)].Id),out var incomingPoint) ? incomingPoint : old;
                            var outgoing=rails.TryGetValue((old,byPair[Key(old,next.Vertex)].Id),out var q) ? q : old;
                            local.Add(Corner(incoming,prev));
                            if (outgoing!=incoming) local.Add(Corner(outgoing,next));
                        }
                    }
                    else local.Add(MeshExtrusion.Copy(c,old));
                    faceRows[(f.Id,old)]=local.ToArray(); row.AddRange(local);
                }
                var cap=result.Faces.First(n=>n.Id==f.Id); cap.Corners=row.ToArray();
                if (f.Corners.Any(c=>affected.Contains(c.Vertex))) changed.Add(f.Id);
                var oldN=Normal(f); var p=cap.Corners.Select(c=>pos[c.Vertex]).ToArray();
                var newN=Enumerable.Range(0,p.Length).Aggregate(Vector3.zero,(sum,i)=>sum+Vector3.Cross(p[i]-p[0],p[(i+1)%p.Length]-p[0]));
                if (Vector3.Dot(oldN,newN)<=1e-10f) throw new InvalidOperationException("Bevel collapses or inverts a face.");
                // A selected-edge offset can invert a surviving rail before area flips.
                for (var i=0;i<f.Corners.Length;i++)
                {
                    var a=f.Corners[i].Vertex; var b=f.Corners[(i+1)%f.Corners.Length].Vertex;
                    var start=faceRows[(f.Id,a)].Last().Vertex; var end=faceRows[(f.Id,b)].First().Vertex;
                    if (Vector3.Dot(pos[end]-pos[start],pos[b]-pos[a])<=1e-10f)
                        throw new InvalidOperationException("Bevel crosses a narrow face. Reduce width.");
                }
            }
            MeshFace Surface(MeshCorner[] corners,int material)
            {
                Budget(result,1,corners.Length);
                var face=new MeshFace { Id=result.NextId++, Material=material, ExtrusionWall=true,
                    Corners=corners.Select(c=>MeshExtrusion.Copy(c,c.Vertex)).ToArray() };
                // New patches have no authored UV island. Interpolated corner UVs can
                // accidentally cancel along a small triangle edge, producing near-zero
                // density. Seed the generated island from a real source face edge.
                var seed = source.Faces.Where(f => f.Material == material &&
                    f.Corners.Any(c => c.Vertex == origin[corners[0].Vertex])).OrderBy(f => f.Id).First();
                var at = Array.FindIndex(seed.Corners, c => c.Vertex == origin[corners[0].Vertex]);
                var from = seed.Corners[at]; var to = seed.Corners[(at + 1) % seed.Corners.Length];
                Vector2 startUV = from.UV[0];
                Vector2 edgeUV = (Vector2)to.UV[0] - startUV;
                var sourceLength = (pos[to.Vertex] - pos[from.Vertex]).magnitude;
                var density = edgeUV.magnitude > 1e-6f && sourceLength > 1e-6f ? edgeUV.magnitude / sourceLength : 1f;
                var direction = edgeUV.magnitude > 1e-6f ? edgeUV.normalized : Vector2.right;
                var firstLength = (pos[corners[1].Vertex] - pos[corners[0].Vertex]).magnitude;
                for (var i = 0; i < face.Corners.Length; i++)
                {
                    var value = face.Corners[i].UV[0];
                    var point = startUV + (i == 1 ? direction * density * firstLength : Vector2.zero);
                    value.x = point.x; value.y = point.y;
                    face.Corners[i].UV[0] = value;
                }
                result.Faces.Add(face); newFaces.Add(face.Id); changed.Add(face.Id); return face;
            }
            if (!vertexOnly)
            foreach (var edge in source.Edges.Where(e=>selected.Contains(e.Id)).OrderBy(e=>e.Id))
            {
                var fs=source.Faces.Where(f=>adjacency[edge.Id].Contains(f.Id)).OrderBy(f=>f.Id).ToArray();
                var f=fs[0]; var i=Enumerable.Range(0,f.Corners.Length).First(j=>Key(f.Corners[j].Vertex,f.Corners[(j+1)%f.Corners.Length].Vertex)==Key(edge.A,edge.B));
                var a=f.Corners[i].Vertex; var b=f.Corners[(i+1)%f.Corners.Length].Vertex;
                if (fs.Length == 2 && Vector3.Dot(Vector3.Cross(Normal(f), Normal(fs[1])), (pos[b]-pos[a]).normalized) <= 1e-6f)
                    throw new InvalidOperationException("Edge Bevel requires a convex dihedral; flat/concave edges are unavailable.");
                var firstA=faceRows[(f.Id,a)].Last(); var firstB=faceRows[(f.Id,b)].First();
                var lastA=fs.Length==2 ? faceRows[(fs[1].Id,a)].First() : MeshExtrusion.Copy(f.Corners[i],a);
                var lastB=fs.Length==2 ? faceRows[(fs[1].Id,b)].Last() : MeshExtrusion.Copy(f.Corners[(i+1)%f.Corners.Length],b);
                MeshCorner[] Arc(MeshCorner start,MeshCorner end,int old)
                {
                    var row=new MeshCorner[segments+1]; row[0]=start; row[segments]=end;
                    var axis=(pos[b]-pos[a]).normalized;
                    var control=pos[old]+axis*Vector3.Dot((pos[start.Vertex]+pos[end.Vertex])*.5f-pos[old],axis);
                    var u=control-pos[start.Vertex]; var v=control-pos[end.Vertex];
                    var weight=Mathf.Sqrt(Mathf.Max(0,(1-Vector3.Dot(u.normalized,v.normalized))*.5f));
                    if (fs.Length==2 && (weight<1e-4f || u.magnitude<1e-6f || v.magnitude<1e-6f))
                        throw new InvalidOperationException("Coplanar/flat selected edges have no bevel profile.");
                    for (var k=1;k<segments;k++)
                    {
                        var t=k/(float)segments; var s=1-t;
                        var p=fs.Length==1 ? Vector3.Lerp(pos[start.Vertex],pos[end.Vertex],t) :
                            (s*s*pos[start.Vertex]+2*weight*s*t*control+t*t*pos[end.Vertex])/(s*s+2*weight*s*t+t*t);
                        row[k]=MeshLoopCut.Interpolate(start,end,Add(p,old),t);
                    }
                    return row;
                }
                var aa=Arc(firstA,lastA,a); var bb=Arc(firstB,lastB,b);
                for (var k=0;k<segments;k++) Surface(new[] {bb[k],aa[k],aa[k+1],bb[k+1]},f.Material);
            }
            // Close only local interior holes. Original open boundaries remain open.
            foreach (var old in affected.OrderBy(i=>i))
            {
                var edges=new Dictionary<(int,int),List<(MeshCorner a,MeshCorner b,int material)>>();
                foreach (var face in result.Faces)
                for (var i=0;i<face.Corners.Length;i++)
                {
                    var a=face.Corners[i]; var b=face.Corners[(i+1)%face.Corners.Length];
                    if (origin[a.Vertex]!=old || origin[b.Vertex]!=old) continue;
                    var key=Key(a.Vertex,b.Vertex);
                    if (!edges.ContainsKey(key)) edges[key]=new List<(MeshCorner,MeshCorner,int)>();
                    edges[key].Add((a,b,face.Material));
                }
                var boundary=edges.Values.Where(e=>e.Count==1).Select(e=>e[0]).ToArray();
                if (boundary.Length==0) continue;
                if (incident[old].Any(e=>adjacency[e.Id].Length==1)) continue;
                if (boundary.Length<3 || boundary.GroupBy(e=>e.b.Vertex).Any(g=>g.Count()!=1))
                    throw new InvalidOperationException("Bevel corner patch branches.");
                var remaining=boundary.ToDictionary(e=>e.b.Vertex);
                var loop=new List<MeshCorner>(); var at=remaining.Keys.Min(); var initial=at;
                while (remaining.TryGetValue(at,out var side))
                {
                    loop.Add(MeshExtrusion.Copy(side.b,at)); remaining.Remove(at); at=side.a.Vertex;
                }
                if (at!=initial || remaining.Count!=0) throw new InvalidOperationException("Bevel corner patch is not one simple loop.");
                if (vertexOnly && segments>1)
                {
                    // Spherical trihedral patch: explicit supported multi-segment vertex case.
                    // Other fans use the planar one-segment truncation, never fake segments.
                    var dirs=incident[old].Select(e=>(pos[e.A==old ? e.B : e.A]-pos[old]).normalized).ToArray();
                    if (dirs.Length!=3 || Enumerable.Range(0,3).Any(i=>Mathf.Abs(Vector3.Dot(dirs[i],dirs[(i+1)%3]))>1e-4f))
                        throw new InvalidOperationException("Multi-segment Vertex Bevel requires an orthogonal trihedral corner. Use one segment.");
                    var center=pos[old]+dirs.Aggregate(Vector3.zero,(sum,d)=>sum+d)*width;
                    var radius=width*Mathf.Sqrt(2);
                    var grid=new Dictionary<(int,int),MeshCorner>();
                    for (var i=0;i<=segments;i++)
                    for (var j=0;j<=segments-i;j++)
                    {
                        var a=i/(float)segments; var b=j/(float)segments; var c=1-a-b;
                        var linear=pos[loop[0].Vertex]*c+pos[loop[1].Vertex]*a+pos[loop[2].Vertex]*b;
                        var point=center+(linear-center).normalized*radius;
                        var original=i==0&&j==0 ? loop[0] : i==segments ? loop[1] : j==segments ? loop[2] : null;
                        var id=original?.Vertex ?? Add(point,old);
                        var corner=MeshExtrusion.Copy(loop[0],id);
                        corner.Normal=loop[0].Normal*c+loop[1].Normal*a+loop[2].Normal*b;
                        corner.Tangent=loop[0].Tangent*c+loop[1].Tangent*a+loop[2].Tangent*b;
                        corner.Color=loop[0].Color*c+loop[1].Color*a+loop[2].Color*b;
                        for (var channel=0;channel<8;channel++) corner.UV[channel]=loop[0].UV[channel]*c+loop[1].UV[channel]*a+loop[2].UV[channel]*b;
                        grid[(i,j)]=corner;
                    }
                    // Insert identical boundary samples in each adjacent authored face.
                    var boundaryRows=new[] { Enumerable.Range(0,segments+1).Select(k=>grid[(k,0)]).ToArray(),
                        Enumerable.Range(0,segments+1).Select(k=>grid[(segments-k,k)]).ToArray(),
                        Enumerable.Range(0,segments+1).Select(k=>grid[(0,segments-k)]).ToArray() };
                    foreach (var row in boundaryRows)
                    foreach (var face in result.Faces.Where(f=>!newFaces.Contains(f.Id)).ToArray())
                    {
                        var index=Enumerable.Range(0,face.Corners.Length).Where(i=>face.Corners[i].Vertex==row.Last().Vertex && face.Corners[(i+1)%face.Corners.Length].Vertex==row[0].Vertex).DefaultIfEmpty(-1).First();
                        if (index<0) continue;
                        var list=face.Corners.ToList();
                        var from=list[index]; var to=list[(index+1)%list.Count];
                        list.InsertRange(index+1,row.Reverse().Skip(1).Take(segments-1).Select((c,k)=>MeshLoopCut.Interpolate(from,to,c.Vertex,(k+1f)/segments)));
                        face.Corners=list.ToArray();
                    }
                    for (var i=0;i<segments;i++)
                    for (var j=0;j<segments-i;j++)
                    {
                        Surface(new[] {grid[(i,j)],grid[(i+1,j)],grid[(i,j+1)]},boundary[0].material);
                        if (i+j<segments-1) Surface(new[] {grid[(i+1,j)],grid[(i+1,j+1)],grid[(i,j+1)]},boundary[0].material);
                    }
                }
                else Surface(loop.ToArray(),boundary[0].material);
            }
            if (vertexOnly && segments>1 && affected.Any(id=>incident[id].Any(e=>adjacency[e.Id].Length==1)))
                throw new InvalidOperationException("Open-boundary Vertex Bevel supports one segment only.");
            var used=result.Faces.SelectMany(f=>Enumerable.Range(0,f.Corners.Length).Select(i=>Key(f.Corners[i].Vertex,f.Corners[(i+1)%f.Corners.Length].Vertex))).ToHashSet();
            result.Edges.RemoveAll(e=>adjacency[e.Id].Length>0 && !used.Contains(Key(e.A,e.B)));
            result.RebuildEdges();
            var referenced=result.Faces.SelectMany(f=>f.Corners.Select(c=>c.Vertex)).Concat(result.Edges.SelectMany(e=>new[] {e.A,e.B})).ToHashSet();
            result.Vertices.RemoveAll(v=>affected.Contains(v.Id)&&!referenced.Contains(v.Id));
            result.SelectedFaces=Array.Empty<int>();
            result.SelectedVertices=vertexOnly ? result.Vertices.Where(v=>affected.Contains(origin[v.Id])).Select(v=>v.Id).ToArray() : Array.Empty<int>();
            result.SelectedEdges=vertexOnly ? Array.Empty<int>() : result.Edges.Where(e=>result.Faces.Any(f=>newFaces.Contains(f.Id)&&Enumerable.Range(0,f.Corners.Length).Any(i=>Key(f.Corners[i].Vertex,f.Corners[(i+1)%f.Corners.Length].Vertex)==Key(e.A,e.B)))).Select(e=>e.Id).ToArray();
            result.ActiveElement=result.SelectedVertices.Concat(result.SelectedEdges).FirstOrDefault();
            var uv=result.Clone(); uv.Faces.RemoveAll(f=>!newFaces.Contains(f.Id));
            MeshAutoUV.UpdateWalls(uv,uv.Clone()); result.UVDimensions[0]=uv.UVDimensions[0];
            foreach (var face in uv.Faces) result.Faces.First(f=>f.Id==face.Id).Corners=face.Corners;
            Budget(result,0,0); result.Validate(); MeshInset.CheckIntersections(result,changed);
            // Closed input fans must remain closed after all patch joins.
            var finalAdj=result.EdgeFaces();
            foreach (var e in result.Edges.Where(e=>affected.Contains(origin[e.A])&&affected.Contains(origin[e.B])))
                if (finalAdj[e.Id].Length>2 || finalAdj[e.Id].Length==1 && incident[origin[e.A]].All(n=>adjacency[n.Id].Length==2) && incident[origin[e.B]].All(n=>adjacency[n.Id].Length==2))
                    throw new InvalidOperationException("Bevel would leave a hole or non-manifold join.");
            return result;
        }
        private static void Budget(MeshDocument source,long faces,long corners)
        {
            if (source.Faces.Count+faces>MeshEditTransaction.FullPreviewFaceBudget || source.Faces.Sum(f=>(long)f.Corners.Length)+corners>MeshEditTransaction.FullPreviewCornerBudget || source.Vertices.Count+corners>300000)
                throw new InvalidOperationException("Bevel exceeds the 250 faces / 1500 corners / 300k vertices preview budget.");
        }
    }
}
