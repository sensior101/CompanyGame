"""Surface graph cuts for offline, reviewed furniture material assignments.
Run with Python 3.12 and PyMaxflow; generated face masks are used by Blender.
"""
import numpy as np
import maxflow
class Surface:
 def __init__(self,data):
  self.data=data; self.c=data['coords']; self.n=data['normals']; self.v=data['vertices']; self.f=data['faces'];self.area=data['areas']; self.count=len(self.f)
  e=np.concatenate((self.f[:,[0,1]],self.f[:,[1,2]],self.f[:,[2,0]])); owners=np.tile(np.arange(len(self.f)),3);e.sort(axis=1);order=np.lexsort((e[:,1],e[:,0]));e=e[order];owners=owners[order];ok=np.all(e[1:]==e[:-1],axis=1);ids=np.where(ok)[0];self.a=owners[ids];self.b=owners[ids+1];self.edge=e[ids]
  length=np.linalg.norm(self.v[self.edge[:,0]]-self.v[self.edge[:,1]],axis=1)/np.ptp(self.v,axis=0).max();dot=np.clip(np.sum(self.n[self.a]*self.n[self.b],axis=1),-1,1);ang=np.arccos(dot)
  self.capacity=length*(.025+np.exp(-np.square(ang/.32)))
 def cut(self,positive,negative,prior=None,prior_weight=.003):
  positive=np.asarray(positive,bool);negative=np.asarray(negative,bool)&~positive
  graph=maxflow.Graph[float](self.count,len(self.a));nodes=graph.add_nodes(self.count)
  graph.add_edges(self.a,self.b,self.capacity,self.capacity)
  # Segment zero is the source/selected surface.
  src=np.zeros(self.count);sink=np.zeros(self.count)
  if prior is not None:
   area=self.area/(np.ptp(self.v,axis=0).max()**2);src=np.where(prior,area*prior_weight,0);sink=np.where(prior,0,area*prior_weight)
  src[positive]=1e6;sink[negative]=1e6
  graph.add_grid_tedges(nodes,src,sink);graph.maxflow();return ~graph.get_grid_segments(nodes)
 def near(self,points,r=.025):
  out=np.zeros(self.count,bool)
  for p in points:out|=np.linalg.norm(self.c-np.array(p),axis=1)<r
  return out
 def components(self):
  parent=np.arange(len(self.v))
  def root(i):
   while parent[i]!=i:parent[i]=parent[parent[i]];i=parent[i]
   return i
  for a,b,c in self.f:
   a=root(a);b=root(b);c=root(c);parent[b]=a;parent[c]=a
  ids=np.array([root(f[0]) for f in self.f]);uniq,cnt=np.unique(ids,return_counts=True);rank=uniq[np.argsort(-cnt)];return np.array([np.where(rank==u)[0][0] for u in ids])
