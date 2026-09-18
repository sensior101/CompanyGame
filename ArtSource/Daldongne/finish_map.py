"""Final wall openings and render settings. Run after refine_map.py."""
wall_cuts=0
for tier,name,x0,x1,y0,y1,z0,z1 in cuts:
    # A cap often spans multiple bricks. Clip it rather than deleting the entire wall.
    for o in list(bpy.data.objects):
        if not o.name.startswith(('Wall_','WallCap_')):continue
        points=[o.matrix_world@Vector(v) for v in o.bound_box]
        lo=[min(v[j] for v in points) for j in range(3)]
        hi=[max(v[j] for v in points) for j in range(3)]
        if hi[0]<=x0 or lo[0]>=x1 or hi[1]<=y0 or lo[1]>=y1 or hi[2]<=z0 or lo[2]>=z1:continue
        cut_box(o.name,'WallOpening_'+name,x0,x1,y0,y1,z0,z1)
        wall_cuts+=1
        if len(o.data.polygons)==0:bpy.data.objects.remove(o,do_unlink=True)

s=bpy.context.scene
s.eevee.taa_render_samples=24
s.render.resolution_x=1100;s.render.resolution_y=1100
s.view_settings.view_transform='Khronos PBR Neutral'
# Organize the editable file into meaningful collections, keeping every prop editable.
categories={}
def category(n):
    if n.startswith(('Terrain','Wall','Rock','Water','Foam')):return '01 Terrain and retaining walls'
    if n.startswith(('Walk','Support_Stair','Rail','Bridge','Paving','Curb','Road','Causeway')):return '02 Roads paths and stairs'
    if n.startswith(('Hamburger','Convenience','Snack','Cafe','Bakery','Laundry','Gosiwon','Home','Building','CityHall','Station','Shelter','Vending','BusStop')):return '03 Buildings and landmarks'
    if n.startswith(('Tree','Garden','Planter','Shrub','Flower','Soil')):return '04 Trees and flower gardens'
    if n.startswith(('TownBus','YellowCar','RedCar','CreamVan','NPC')):return '05 Vehicles and scale figures'
    if n.startswith(('Sun','Sky','Warm','Camera')):return '07 Cameras and lighting'
    return '06 Street furniture'
for o in list(s.objects):
    key=category(o.name)
    if key not in categories:
        col=bpy.data.collections.get(key) or bpy.data.collections.new(key)
        if col.name not in s.collection.children:s.collection.children.link(col)
        categories[key]=col
    col=categories[key]
    if o.name not in col.objects:col.objects.link(o)
    for existing in list(o.users_collection):
        if existing!=col:existing.objects.unlink(o)
result={'wall_openings':wall_cuts,'objects':len(s.objects),'collections':list(categories),'triangles_instanced':sum(len(p.vertices)-2 for o in s.objects if o.type=='MESH' for p in o.data.polygons)}
