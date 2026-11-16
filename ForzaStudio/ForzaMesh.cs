/*
    This is applicate to all source code of all project
    
    Forza Studio: Forza Motorsport resource extraction tool.

    Copyright (C) 2012 Mike Davis (http://codeescape.com/)

    Permission is hereby granted, free of charge, to any person 
    obtaining a copy of this software and associated documentation
    files (the "Software"), to deal in the Software without restriction,
    including without limitation the rights to use, copy, modify, merge,
    publish, distribute, sublicense, and/or sell copies of the Software,
    and to permit persons to whom the Software is furnished to do so,
    subject to the following conditions:

    The above copyright notice and this permission notice shall be included 
    in all copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
    OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
    THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
    FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
    DEALINGS IN THE SOFTWARE.
*/

namespace ForzaStudio
{
    using Microsoft.Xna.Framework;
    using Microsoft.Xna.Framework.Graphics;
    using System;

    public class ForzaMesh : IDisposable
    {
        // One vertex declaration per device, made once. A new one on every draw was never released (XNA keeps a
        // graphics resource alive until it is disposed): hundreds per redraw, so the editor slowed down and went
        // wrong soon after editing, when the handles redraw on every mouse move.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GraphicsDevice, VertexDeclaration> declarations =
            new System.Runtime.CompilerServices.ConditionalWeakTable<GraphicsDevice, VertexDeclaration>();

        private static VertexDeclaration DeclarationFor(GraphicsDevice device)
        {
            return declarations.GetValue(device, d => new VertexDeclaration(d, ForzaVertex.VertexElements));
        }

        public int[] Indices;
        public string MaterialName;
        public string Technique;   // shader technique the game runs it with (cockpit files add cockpit_), null = MaterialName
        public string Name;
        public ForzaVertex[] Vertices;
        // Position in the carbin (section, subsection in file order), -1 if unknown: lets an edit
        // reach this one mesh when several in a part share the material name.
        public int SectionIndex = -1;
        public int SubIndex = -1;

        public ForzaMesh(string name, string materialName, int[] indices, ForzaVertex[] vertices)
        {
            this.Name = name;
            this.MaterialName = materialName;
            this.Indices = indices;
            this.Vertices = vertices;
        }

        public void Dispose()
        {
            this.Name = null;
            this.MaterialName = null;
            this.Indices = null;
            this.Vertices = null;
            this.displayVertices = null;
        }

        // Draw with the second UV set in TEXCOORD0 (some materials map their texture with UV2).
        public bool UseSecondUV;

        // Live preview of a pending part edit: drawn with this extra world transform, tinted when
        // highlighted. The vertices themselves are untouched until the edit is applied.
        public Matrix PreviewTransform = Matrix.Identity;
        public int[] PreviewVertices;                       // MESH tab drag: the points that move (indices into Vertices)
        public Matrix PreviewVertexMatrix = Matrix.Identity;
        public bool Highlight;
        public System.Collections.Generic.Dictionary<string, float[]> Settings;   // the car's shadersettings.xml values for this mesh's technique (ShaderLook.Apply)
        public bool Unused;       // no FM4 model of this kind and LOD uses the material (Fm4MaterialUse): drawn magenta
        public bool Selected;      // the node selected in the model tree (or one under it): lit up in the viewport
        public bool ForceTexture;   // the UV mapper's texture is drawn whatever the material's look says (paint, glass...)
        public bool Frozen;     // drawn dark grey and not picked by a click in the viewport (right click > Freeze Selection)
        public bool Hidden;     // "show only this part": the other parts are not drawn
        private ForzaVertex[] displayVertices;
        private bool displaySecondUV;
        public float UvScale;           // texture repeat from the car's shadersettings.xml (ShaderLook.Apply), 0 = none
        private float displayUvScale;

        // The vertices or their coordinates changed: the copy the viewer draws is made again.
        public void Invalidate()
        {
            this.displayVertices = null;
        }

        public void Draw(ref GraphicsDevice device)
        {
            // FH stripped_*.carbin have no vertex data: drawing nothing, a failed draw inside an effect pass
            // would leave the device half-begun and break every model drawn after it.
            if (this.Vertices == null || this.Vertices.Length == 0 || this.Indices == null || this.Indices.Length < 3)
            {
                return;
            }
            // Parsed UVs are bottom-up (OBJ convention); Direct3D samples top-down.
            if (this.displayVertices == null || this.displaySecondUV != this.UseSecondUV || this.displayUvScale != this.UvScale)
            {
                this.displayVertices = (ForzaVertex[]) this.Vertices.Clone();
                float scale = (this.UvScale > 0f) ? this.UvScale : 1f;
                for (int i = 0; i < this.displayVertices.Length; i++)
                {
                    Vector2 uv = this.UseSecondUV ? this.displayVertices[i].texture1 : this.displayVertices[i].texture0;
                    this.displayVertices[i].texture0 = new Vector2(uv.X * scale, (1f - uv.Y) * scale);
                }
                this.displaySecondUV = this.UseSecondUV;
                this.displayUvScale = this.UvScale;
            }
            ForzaVertex[] vertices = this.displayVertices;
            if (this.PreviewVertices != null && this.PreviewVertices.Length > 0)
            {
                // a sub-object drag in the MESH tab: only these points follow the handle until the button is released
                vertices = (ForzaVertex[]) vertices.Clone();
                foreach (int i in this.PreviewVertices)
                {
                    vertices[i].position = Vector3.Transform(vertices[i].position, this.PreviewVertexMatrix);
                }
            }
            device.VertexDeclaration = DeclarationFor(device);
            device.DrawUserIndexedPrimitives<ForzaVertex>(PrimitiveType.TriangleList, vertices, 0, vertices.Length, this.Indices, 0, this.FaceCount);
        }

        ~ForzaMesh()
        {
            this.Dispose();
        }

        public int FaceCount
        {
            get
            {
                return (this.Indices == null) ? 0 : (this.Indices.Length / 3);
            }
        }

        public int VertexCount
        {
            get
            {
                return (this.Vertices == null) ? 0 : this.Vertices.Length;
            }
        }
    }
}

