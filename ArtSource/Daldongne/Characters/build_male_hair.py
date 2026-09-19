"""Short layered companion hairstyle, sharing the approved face and cap fit."""

# A closed overlapping underlayer fills the back and temple gaps beneath locks.
# The front opening leaves the same forehead, eyes and ears readable as the girl.
hair_levels=[
    (1.580,.242,.202,-.019,.73),
    (1.512,.249,.210,-.019,.76),
    (1.436,.245,.201,-.026,.90),
    (1.368,.222,.174,-.047,1.08),
    (1.302,.155,.105,-.075,1.24)]
vertices=[];faces=[];segments=24
for y,rx,rz,cz,opening in hair_levels:
    for i in range(segments+1):
        a=opening+(math.tau-2*opening)*i/segments
        vertices.append((rx*math.sin(a),y,cz+rz*math.cos(a)))
for j in range(len(hair_levels)-1):
    for i in range(segments):
        a=j*(segments+1)+i
        faces.append((a,a+1,a+segments+2,a+segments+1))
underlayer=mesh('Cropped continuous hair underlayer',vertices,faces,'Hair','Hair',True)
bpy.context.view_layer.objects.active=underlayer
modifier=underlayer.modifiers.new('Closed hair edge','SOLIDIFY');modifier.thickness=.025
bpy.ops.object.modifier_apply(modifier=modifier.name)

# Broad tapered tufts create a soft layered silhouette instead of a straight bob.
for i in range(11):
    a=.93+(math.tau-1.86)*i/10
    sx=math.sin(a);sz=math.cos(a)
    bottom=1.311+.025*abs(sx)+.012*(i%2)
    loft('Short back layer %02d'%i,[
        (.230*sx,1.579,-.025+.187*sz,.027,.028),
        (.250*sx,1.515,-.028+.213*sz,.037,.040),
        (.247*sx,1.432,-.040+.205*sz,.041,.045),
        (.227*sx,1.363,-.055+.177*sz,.037,.041),
        (.200*sx,bottom,-.066+.145*sz,.005,.010)],
        'HairHighlight' if i in (2,8) else 'Hair','Hair',8,True)

for s in (-1,1):
    label='Left' if s<0 else 'Right'
    loft(label+' swept temple lock',[
        (s*.174,1.596,.081,.027,.029),
        (s*.214,1.532,.118,.043,.041),
        (s*.233,1.470,.099,.034,.037),
        (s*.238,1.423,.068,.023,.029),
        (s*.218,1.384,.066,.004,.009)],'Hair','Head',10,True)
    loft(label+' small nape tuft',[
        (s*.174,1.434,-.161,.028,.032),
        (s*.175,1.354,-.156,.032,.037),
        (s*.159,1.290,-.127,.004,.008)],'Hair','Hair',8,True)
