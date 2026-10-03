using UnityEngine;

/// <summary>Small font-independent inventory pictograms.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class InventoryGlyphGraphic : UnityEngine.UI.MaskableGraphic
{
    public enum Glyph { Person, Top, Bottom, Socks, Shoes, Pet, Bag, Coin, Heart, Energy, Stress }
    public Glyph kind;
    protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
    {
        mesh.Clear();
        switch (kind)
        {
            case Glyph.Person:
                Oval(mesh, .5f, .73f, .22f, .22f);
                Polygon(mesh, new[] { V(.12f,.05f), V(.17f,.39f), V(.31f,.52f), V(.69f,.52f), V(.83f,.39f), V(.88f,.05f) });
                break;
            case Glyph.Top:
                Rectangle(mesh,.3f,.06f,.4f,.76f);
                Polygon(mesh,new[]{V(.08f,.67f),V(.26f,.91f),V(.41f,.92f),V(.38f,.57f),V(.25f,.52f)});
                Polygon(mesh,new[]{V(.92f,.67f),V(.74f,.91f),V(.59f,.92f),V(.62f,.57f),V(.75f,.52f)});
                break;
            case Glyph.Bottom:
                Rectangle(mesh,.2f,.64f,.6f,.29f);
                Polygon(mesh,new[]{V(.2f,.67f),V(.5f,.67f),V(.43f,.07f),V(.12f,.07f)});
                Polygon(mesh,new[]{V(.5f,.67f),V(.8f,.67f),V(.88f,.07f),V(.57f,.07f)});
                break;
            case Glyph.Socks:
                Rectangle(mesh,.13f,.2f,.24f,.7f); Oval(mesh,.33f,.17f,.23f,.13f);
                Rectangle(mesh,.56f,.35f,.23f,.61f); Oval(mesh,.74f,.32f,.22f,.13f);
                break;
            case Glyph.Shoes:
                Rectangle(mesh,.1f,.14f,.28f,.49f); Oval(mesh,.51f,.19f,.41f,.14f);
                Rectangle(mesh,.47f,.49f,.23f,.42f); Oval(mesh,.74f,.48f,.23f,.13f);
                break;
            case Glyph.Pet:
                Oval(mesh,.5f,.32f,.29f,.25f); Oval(mesh,.18f,.58f,.115f,.15f);
                Oval(mesh,.38f,.79f,.115f,.16f); Oval(mesh,.64f,.79f,.115f,.16f); Oval(mesh,.84f,.58f,.11f,.15f);
                break;
            case Glyph.Bag:
                Polygon(mesh,new[]{V(.2f,.73f),V(.8f,.73f),V(.94f,.06f),V(.06f,.06f)});
                Rectangle(mesh,.34f,.7f,.09f,.24f); Rectangle(mesh,.57f,.7f,.09f,.24f); Rectangle(mesh,.34f,.85f,.32f,.09f);
                break;
            case Glyph.Coin: Oval(mesh,.5f,.5f,.46f,.46f); break;
            case Glyph.Heart:
                Oval(mesh,.30f,.68f,.25f,.24f); Oval(mesh,.70f,.68f,.25f,.24f);
                Polygon(mesh,new[]{V(.07f,.61f),V(.93f,.61f),V(.5f,.08f)});
                break;
            case Glyph.Energy:
                Polygon(mesh,new[]{V(.55f,.97f),V(.15f,.43f),V(.54f,.43f)});
                Polygon(mesh,new[]{V(.46f,.57f),V(.85f,.57f),V(.38f,.03f)});
                break;
            case Glyph.Stress:
                // A tense face with angled brows: distinct from the heart and lightning.
                Oval(mesh,.5f,.48f,.44f,.43f);
                break;
        }
    }
    static Vector2 V(float x,float y) => new Vector2(x,y);
    void Rectangle(UnityEngine.UI.VertexHelper mesh,float x,float y,float w,float h)
        => Polygon(mesh,new[]{V(x,y),V(x+w,y),V(x+w,y+h),V(x,y+h)});
    void Oval(UnityEngine.UI.VertexHelper mesh,float x,float y,float rx,float ry)
    {
        var points = new Vector2[24];
        for(int i=0;i<points.Length;i++){float a=i*Mathf.PI*2f/points.Length;points[i]=V(x+Mathf.Cos(a)*rx,y+Mathf.Sin(a)*ry);}
        Polygon(mesh,points);
    }
    void Polygon(UnityEngine.UI.VertexHelper mesh,Vector2[] points)
    {
        Rect r=GetPixelAdjustedRect(); int first=mesh.currentVertCount;
        Vector2 centre=Vector2.zero;
        foreach(var point in points)centre+=point;
        centre/=points.Length;
        mesh.AddVert(new Vector3(r.xMin+centre.x*r.width,r.yMin+centre.y*r.height),color,Vector2.zero);
        foreach(var point in points)mesh.AddVert(new Vector3(r.xMin+point.x*r.width,r.yMin+point.y*r.height),color,Vector2.zero);
        for(int i=0;i<points.Length;i++)mesh.AddTriangle(first,first+1+i,first+1+(i+1)%points.Length);
    }
}
