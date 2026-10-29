"""Export placed original static stage geometry with UVs, materials and textures.

Writes Assets/_Project/Stages/Source/{<mesh>.bytes,stages.json,report.json} and
the original textures as PNG to Assets/_Project/Stages/Textures. Every material
that ends up without a base-color texture is listed in report.json with a reason.

Mesh format STG2 (Unity space, metres): vertex count, section count, UV count,
positions, normals, tangents (xyzw), UV channels (V flipped), then per section
its material slot, index count and indices (Unreal winding; MuseumStageImporter reverses
each triangle for Unity).
"""
import hashlib,io,json,re,struct,sys
from pathlib import Path
import numpy as np
ROOT=Path(__file__).resolve().parents[3]
PROJECT=Path(__file__).resolve().parents[1]
sys.path[:0]=[str(ROOT/'t7extract'),str(PROJECT/'Tools/OriginalLighting')]
from t7extract.cli import Source
from t7extract.pak import TEKKEN_ASSET_KEY
from t7extract.package import Package, read_properties
from t7extract.mesh import extract_mesh
from t7extract.anim import import_path, game_path_to_virtual
from t7extract.gltf import _struct_array, _unpack_normals, _normalize, _uvs
from t7extract.texture import read_texture, to_image
from t7extract.export_cli import _resolver
from extract_stage_lighting import properties

OUT=PROJECT/'Assets/_Project/Stages/Source'
TEXTURES=PROJECT/'Assets/_Project/Stages/Textures'
MAX_TEXTURE=2048
BASE_PARAMS=('basecolormap','basecolor','base color','base_color','diffuse','diffusemap','diffuse map','albedo','color map','colormap','ground map','texture','tex','d')
NORMAL_PARAMS=('normalmap','normal','normal map','nrm','n')
EMISSIVE_PARAMS=('emissivemap','emissive','emissive map','emission','glow')

def role(param,path):
 """Classify a texture by parameter name, then by the texture's suffix."""
 p=param.lower().strip(); name=path.rsplit('/',1)[-1].lower()
 if any(w in p for w in ('wet','detail','cube','mask','rough','metal','spec','ao','height','noise','flow','distort','lightmap','reflection')):return None
 if p in NORMAL_PARAMS or 'normal' in p:return 'normal'
 if p in EMISSIVE_PARAMS or 'emiss' in p:return 'emissive'
 if p in BASE_PARAMS or any(w in p for w in ('diffuse','basecolor','base color','albedo','ground')):return 'base'
 return suffix_role(name)

# Layered materials blend an overlay (moss, a second material, the top of a landscape blend)
# over a primary layer; the primary layer is the colour map.
OVERLAY=re.compile(r'(moss|_b$|^b_|baseb|mat2|top |add|decal|detail|cherry|snow|dirt|noise|flow)')
def layer_rank(param):
 p=param.lower()
 if OVERLAY.search(p):return 2
 if re.search(r'(^basecolor(map|_a)?$|^diffuse(map)?$|^base ?color$|mat1|basea|^basetex$|bottom diffuse)',p):return 0
 return 1

def suffix_role(name):
 name=name.lower()
 # SpeedTree trees name their maps X_Tex, X_Normal_Tex, X_Spec_Tex (optionally _2).
 name=re.sub(r'_tex(_\d+)?$','',name)
 if 'normal' in name:return 'normal'
 if re.search(r'(spec|gloss|gradation|^defaultnormal)',name):return None
 if re.search(r'_(n|nrm|normal|nml)\d*$',name):return 'normal'
 if re.search(r'_(e|em|emissive|glow)\d*$',name):return 'emissive'
 if re.search(r'_(d|bc|diffuse|dif|base|albedo|col|color|c)\d*[a-z]?$',name):return 'base'
 return None

class Materials:
 def __init__(self,resolve):
  self.resolve=resolve;self.entries={};self.textures={};self.texture_failures={};self._packed={}

 def chain(self,path,depth=0):
  """(package, export, props) for a material instance and each parent."""
  if depth>8 or not path or not path.startswith(('/Game/','/Engine/')):return []
  data=self.resolve(game_path_to_virtual(path))
  if data is None:return []
  pkg=Package(data,path);name=path.rsplit('/',1)[-1]
  export=next((e for e in pkg.exports if e.object_name==name),None) or next((e for e in pkg.exports if 'Material' in e.class_name),None)
  if export is None:return []
  props=read_properties(pkg.reader(export.serial_offset))
  out=[(pkg,export,props)]
  parent=props.get('Parent')
  if isinstance(parent,int) and parent<0:out+=self.chain(import_path(pkg,parent),depth+1)
  return out

 def key(self,path):
  path=path.split('.')[0]
  if path in self.entries:return self.entries[path]['key']
  name=re.sub(r'[^A-Za-z0-9_]+','_',path.rsplit('/',1)[-1])[:60]
  key=name+'_'+hashlib.sha1(path.lower().encode()).hexdigest()[:6]
  entry={'key':key,'path':path,'base':'','normal':'','emissive':'','blend':'opaque','twoSided':False,'hidden':bool(HELPERS.search(path.rsplit('/',1)[-1])),
         'color':[1,1,1,1],'emissiveColor':[0,0,0,1],'parameters':{},'baseSource':'','note':''}
  self.entries[path]=entry
  if entry['hidden']:entry['note']='editor helper material, not drawn in the game';return key
  try:self.describe(path,entry)
  except Exception as ex:entry['note']='material package unreadable: '+str(ex)
  return key

 def describe(self,path,entry):
  chain=self.chain(path)
  if not chain:entry['note']='material package not found in the paks';return
  params={};vectors={};imports=[]
  for pkg,export,props in chain:
   tag=props.get('TextureParameterValues')
   if tag is not None and getattr(tag,'inner_type','')=='StructProperty':
    for el in _struct_array(pkg,tag):
     p,v=el.get('ParameterName'),el.get('ParameterValue')
     if isinstance(p,str) and isinstance(v,int) and v<0:
      tp=import_path(pkg,v)
      if tp:params.setdefault(p,tp)
   tag=props.get('VectorParameterValues')
   if tag is not None and getattr(tag,'inner_type','')=='StructProperty':
    for el in _struct_array(pkg,tag):
     p,v=el.get('ParameterName'),el.get('ParameterValue')
     if isinstance(p,str) and hasattr(v,'value_offset') and v.size==16:
      vectors.setdefault(p,list(struct.unpack_from('<4f',pkg.data,v.value_offset)))
   over=props.get('BasePropertyOverrides')
   if over is not None and getattr(over,'type','')=='StructProperty':
    o=read_properties(pkg.reader(over.value_offset))
    if o.get('bOverride_TwoSided'):entry['twoSided']=entry['twoSided'] or bool(o.get('TwoSided'))
    if o.get('bOverride_BlendMode') and entry['blend']=='opaque':entry['blend']=blend_name(o.get('BlendMode'))
   if export.class_name=='Material':
    entry['twoSided']=entry['twoSided'] or bool(props.get('TwoSided'))
    if entry['blend']=='opaque':entry['blend']=blend_name(props.get('BlendMode'))
    for i,imp in enumerate(pkg.imports):
     if imp.class_name=='Texture2D':
      tp=import_path(pkg,-i-1)
      if tp:imports.append(tp)
  entry['parameters']={k:v.rsplit('/',1)[-1] for k,v in params.items()}
  for slot in ('base','normal','emissive'):
   for p,tp in sorted(params.items(),key=lambda kv:layer_rank(kv[0])):
    if role(p,tp)==slot and not entry[slot]:
     k=self.texture(tp,slot=='normal')
     if k:entry[slot]=k;entry[slot+'Source']='parameter '+p if slot=='base' else ''
  # Base materials sample fixed textures; their cooked package still imports them.
  if not entry['base']:
   for tp in imports:
    if suffix_role(tp.rsplit('/',1)[-1])=='base' and not re.search(r'(msk|mask|nois|noiz)',tp.rsplit('/',1)[-1].lower()):
     k=self.texture(tp,False)
     if k and not self.packed(k):entry['base']=k;entry['baseSource']='parent material sampler '+tp.rsplit('/',1)[-1];break
  # Some packs name albedo _a and keep a packed mask in _c (the ppeel ivy: Ivy_Small_a / _c).
  if not entry['base']:
   for tp in list(params.values())+imports:
    if re.search(r'_a\d*$',tp.rsplit('/',1)[-1].lower()):
     k=self.texture(tp,False)
     if k and not self.packed(k):entry['base']=k;entry['baseSource']='albedo texture '+tp.rsplit('/',1)[-1];break
  if not entry['normal']:
   for tp in imports:
    if suffix_role(tp.rsplit('/',1)[-1])=='normal':
     k=self.texture(tp,True)
     if k:entry['normal']=k;break
  # A lone texture without a role suffix (e.g. tree_01, moon_testB2) is the colour map.
  if not entry['base']:
   for tp in list(params.values())+imports:
    n=tp.rsplit('/',1)[-1].lower()
    if suffix_role(n) is None and not tp.startswith('/Engine/') and not re.search(r'(mask|spec|gloss|normal|gradation|occ|rough|metal|noise|_m\d*$|_s\d*$|_r\d*$|_a\d*$|_h\d*$|cube|daylight|lightmap)',n):
     k=self.texture(tp,False)
     if k and not self.packed(k):entry['base']=k;entry['baseSource']='unsuffixed texture '+tp.rsplit('/',1)[-1];break
  # Self-lit skies and signs carry their picture in the emissive map.
  if not entry['base'] and entry['emissive']:
   entry['base']=entry['emissive'];entry['baseSource']='emissive map (unlit surface)'
  # Sky domes are unlit in the game; their colour map is the emitted picture.
  name=path.rsplit('/',1)[-1].lower()
  if entry['base'] and re.search(r'sky(dome|sphere|box)|_sky$|_sky_',name) and entry['blend']=='opaque':
   entry['emissive']=entry['base'];entry['baseSource']='emissive map (unlit sky) '+entry['baseSource']
  # Fog, smoke and waterfall cards: the greyscale picture is the opacity, cut by the mask texture.
  if entry['base'] and entry['blend'] in ('translucent','additive'):
   base_path=next((tp for tp in list(params.values())+imports if self.textures.get((tp,False))==entry['base']),None)
   mask=next((tp for p,tp in params.items() if 'mask' in p.lower()),None)
   if base_path:
    k=self.texture(base_path,False,alpha_mask=mask or '')
    if k:entry['base']=k
  for p,v in vectors.items():
   pl=p.lower()
   if any(w in pl for w in ('basecolor','base color','diffuse','albedo','color')) and 'emis' not in pl and 'fog' not in pl:
    entry['color']=[max(0.0,min(4.0,x)) for x in v];break
  for p,v in vectors.items():
   if 'emis' in p.lower():entry['emissiveColor']=[max(0.0,x) for x in v];break
  if not entry['base']:
   if not params and not imports:entry['note']='no texture parameters or samplers (shader-constant colour)'
   elif all('cube' in p.lower() or 'reflection' in p.lower() for p in params) and not imports:entry['note']='only reflection cube maps'
   else:
    failed=[self.texture_failures[t] for t in list(params.values())+imports if t in self.texture_failures]
    entry['note']=('base texture failed to decode: '+failed[0]) if failed else 'no recognisable base-colour texture among: '+', '.join(sorted({t.rsplit('/',1)[-1] for t in list(params.values())+imports}))[:400]

 def packed(self,key):
  """True for a channel-packed mask (empty red channel), which can't be a colour map."""
  if key not in self._packed:
   from PIL import Image
   try:a=np.asarray(Image.open(TEXTURES/(key+'.png')).convert('RGB').reduce(8))
   except Exception:a=None
   self._packed[key]=a is not None and a[...,0].max()<4 and a[...,1:].max()>32
  return self._packed[key]

 def texture(self,path,normal,alpha_mask=None):
  """PNG key for a texture. alpha_mask (a texture path, or '' for none) adds an opacity
  channel from the picture's brightness, multiplied by the mask, when it has no real alpha."""
  ck=(path,normal) if alpha_mask is None else (path,normal,alpha_mask)
  if ck in self.textures:return self.textures[ck]
  self.textures[ck]=''
  name=re.sub(r'[^A-Za-z0-9_]+','_',path.rsplit('/',1)[-1])[:70]
  key=name+'_'+hashlib.sha1(path.lower().encode()).hexdigest()[:6]+('_nrm' if normal else '')
  if alpha_mask is not None:
   key+='_op'+(hashlib.sha1(alpha_mask.lower().encode()).hexdigest()[:4] if alpha_mask else '')
   file=TEXTURES/(key+'.png')
   if not file.exists():
    try:
     from PIL import Image
     src=self.texture(path,False)
     if not src:return ''
     img=Image.open(TEXTURES/(src+'.png')).convert('RGBA');a=np.asarray(img).astype(np.float32)
     if a[...,3].min()<250:self.textures[ck]=src;return src          # already has its own alpha
     op=(a[...,0]*.3+a[...,1]*.59+a[...,2]*.11)/255
     if alpha_mask:
      mk=self.texture(alpha_mask,False)
      if mk:
       m=np.asarray(Image.open(TEXTURES/(mk+'.png')).convert('L').resize(img.size)).astype(np.float32)/255
       op=op*m
     a[...,3]=np.clip(op*255,0,255)
     Image.fromarray(a.astype(np.uint8),'RGBA').save(file,'PNG',compress_level=3)
    except Exception as ex:
     self.texture_failures[path]=f'{path.rsplit("/",1)[-1]} opacity: {ex}';return ''
   self.textures[ck]=key;return key
  file=TEXTURES/(key+'.png')
  if not file.exists():
   try:
    data=self.resolve(game_path_to_virtual(path))
    if data is None:raise ValueError('texture package not found')
    pkg=Package(data,path)
    export=next((e for e in pkg.exports if e.class_name=='Texture2D'),None)
    if export is None:raise ValueError('not a 2D texture')
    img=to_image(read_texture(pkg,export),reconstruct_normal_z=normal)
    if normal:
     a=np.asarray(img.convert('RGB')).copy();a[...,1]=255-a[...,1]   # DirectX green -> Unity
     from PIL import Image;img=Image.fromarray(a,'RGB')
    if max(img.size)>MAX_TEXTURE:
     r=MAX_TEXTURE/max(img.size);img=img.resize((max(1,int(img.width*r)),max(1,int(img.height*r))))
    if img.mode not in ('RGB','RGBA','L'):img=img.convert('RGBA')
    img.save(file,'PNG',compress_level=3)
   except Exception as ex:
    self.texture_failures[path]=f'{path.rsplit("/",1)[-1]}: {ex}';return ''
  self.textures[ck]=key;return key

def actor_hidden(p,e):
 if e.outer_index<=0:return False
 try:a,_=properties(p,p.reader(p.exports[e.outer_index-1].serial_offset))
 except Exception:return False
 return bool(a.get('bHidden',False))

# Editor/helper materials that the game never shows (collision, occluders, light bounce cards).
HELPERS=re.compile(r'(^M_Collision$|^M_NoPixelDraw$|^M_lighting\d*$|^BOUNCEMAT\d*$|SKYLIGHTCARD)',re.I)

def blend_name(v):
 if v in (1,'BLEND_Masked'):return 'masked'
 if v in (2,'BLEND_Translucent'):return 'translucent'
 if v in (3,'BLEND_Additive'):return 'additive'
 if v in (4,'BLEND_Modulate'):return 'modulate'
 return 'opaque'

def write_mesh(file,m):
 if m['kind']=='skeletal':return write_skeletal(file,m)
 rig=m['rig'];count=len(m['positions']);raw=np.frombuffer(rig['vertex_data'],np.uint8).reshape(count,rig['stride'])
 if rig['high_precision_tangents']:
  tx=np.ascontiguousarray(raw[:,0:8]).view('<i2').reshape(-1,4)/32767.0;tz=np.ascontiguousarray(raw[:,8:16]).view('<i2').reshape(-1,4)/32767.0;off=16
 else:tx,tz=_unpack_normals(raw[:,0:4]),_unpack_normals(raw[:,4:8]);off=8
 uvs=_uvs(raw,off,min(rig['uv_count'],2),rig['full_precision_uvs'])
 return write_stg2(file,m,tx,tz,uvs,m['sections'])

# Destructible floors (breakable stages) and skeletal props are drawn in their rest pose: the
# vertices are already in mesh space, so the bones are not needed.
def write_skeletal(file,m,pkg_data=None):
 rig=m['rig'];count=m['vertex_count']
 raw=np.frombuffer(m['_data'],np.uint8,count*rig['stride'],rig['vertex_offset']).reshape(count,rig['stride'])
 tx,tz=_unpack_normals(raw[:,0:4]),_unpack_normals(raw[:,4:8])
 uvs=_uvs(raw,rig['position_offset']+12,min(m['uv_channels'],2),rig['full_precision_uvs'])
 sections=[s for s in m['sections'] if not s['disabled']]
 return write_stg2(file,m,tx,tz,uvs,sections)

def write_stg2(file,m,tx,tz,uvs,sections):
 def conv(v):return np.stack([v[:,0],v[:,2],-v[:,1]],axis=1)
 pos=conv(np.asarray(m['positions'],np.float64))*.01
 nrm=_normalize(conv(tz[:,:3]));tan=_normalize(conv(tx[:,:3]))
 # UE keeps the binormal sign in TangentZ.w. Flipping V (and the normal maps' green)
 # reverses the bitangent, so Unity's tangent.w is the opposite sign.
 w=np.where(tz[:,3]<0,1.0,-1.0)
 sections=[s for s in sections if s['index_count']]
 idx=np.asarray(m['indices'],np.int64)
 with file.open('wb') as f:
  f.write(struct.pack('<4siii',b'STG2',len(pos),len(sections),len(uvs)))
  f.write(pos.astype('<f4').tobytes());f.write(nrm.astype('<f4').tobytes())
  f.write(np.concatenate([tan,w[:,None]],axis=1).astype('<f4').tobytes())
  for uv in uvs:
   uv=uv.astype(np.float32).copy();uv[:,1]=1-uv[:,1];f.write(np.nan_to_num(uv).astype('<f4').tobytes())
  for s in sections:
   f.write(struct.pack('<ii',s['material_index'],s['index_count']))
   f.write(idx[s['first_index']:s['first_index']+s['index_count']].astype('<i4').tobytes())
 return sections

MESH_CLASSES=('StaticMesh','DestructibleMesh','SkeletalMesh')
# _light/_lightstatic: the stage's lights, sky light, fog and post process (plus bake helpers,
# hidden by HELPERS, and more light shafts).
SUBLEVELS=('_reload','_demo','_effect','_light','_lightstatic')
COMPONENTS=('StaticMeshComponent','DestructibleComponent','SkeletalMeshComponent','SceneComponent','TekkenPlayerStart')

def collect(stage,level,p,nodes,starts,cache,meshes,mats,inventory,failures,env=None):
 """Append one level's placed mesh components to nodes (ids offset per level)."""
 base=level*100000
 for idx,e in enumerate(p.exports,1):
  # Any other component is kept as a transform-only node: meshes attach to capsules (stg02 rocks,
  # waterfalls, vine bridge) and billboards (god rays), and need those parents to be placed.
  if e.class_name=='PostProcessVolume' and env is not None:
   post_volume(p,e,env)
   continue
  if e.class_name not in COMPONENTS and not e.class_name.endswith('Component'):continue
  try:v,_=properties(p,p.reader(e.serial_offset))
  except Exception as ex:failures.append({'stage':stage,'object':e.object_name,'error':str(ex)});continue
  # Blueprint components serialise only what the instance changes; the rest (a light's intensity
  # and offset, a mesh) is in the blueprint's template (the archetype).
  v={**archetype(p,e,mats.resolve),**v}
  if e.class_name=='TekkenPlayerStart':
   if 'GAME_START' in e.object_name:starts.append((e.object_name,v.get('RootComponent',{}).get('index',0)))
   continue
  ref=(v.get('StaticMesh') or v.get('SkeletalMesh') or {}).get('object','') or '';mesh='';override=[];beam=None
  # Shadow-only proxies (bCastHiddenShadow) and components of hidden actors never draw in the game.
  if ref and not v.get('bHiddenInGame',False) and v.get('bVisible',True) and not v.get('bCastHiddenShadow',False) and not actor_hidden(p,e):
   meshpath=ref.split('.')[0].replace('/Game/','TekkenGame/Content/')+'.uasset'
   if '/LightBeam/Meshes/SM_GodRay' in meshpath:
    beam=god_ray(p,v,mats)
    meshpath=''
   if meshpath and meshpath.lower() not in cache:
    try:
     mp=Package(inventory[meshpath.lower()].read(),meshpath)
     mi=next(i for i,x in enumerate(mp.exports) if x.class_name in MESH_CLASSES)
     if mp.exports[mi].class_name=='StaticMesh':m=extract_mesh(mp,mi,full=True)
     else:
      from t7extract.mesh import _skeletal
      m=_skeletal(mp,mp.exports[mi],True);m['_data']=mp.data
     key=hashlib.sha256(meshpath.encode()).hexdigest()[:20]
     write_mesh(OUT/(key+'.bytes'),m)
     slots=[]
     for r in m['rig']['materials']:
      mpth=import_path(mp,r) if r<0 else None
      slots.append(mats.key(mpth) if mpth else '')
     meshes[key]={'key':key,'path':meshpath,'materials':slots}
     cache[meshpath.lower()]=key
    except Exception as ex:
     cache[meshpath.lower()]='';failures.append({'stage':stage,'mesh':meshpath,'error':str(ex)})
   mesh=cache[meshpath.lower()] if meshpath else ''
   for o in v.get('OverrideMaterials',[]) or []:
    obj=o.get('object') or ''
    override.append(mats.key(obj) if o.get('index',0)<0 and obj.startswith('/') else '')
  # Particle systems (brazier fires, falling sand, dust): the template name; Tools/StageEffects
  # decodes the systems and the stage prefab plays them.
  effect=''
  if e.class_name=='ParticleSystemComponent' and v.get('bAutoActivate',True) and not v.get('bHiddenInGame',False) and not actor_hidden(p,e):
   effect=((v.get('Template') or {}).get('object','') or '').split('.')[0].rsplit('/',1)[-1]
  extras={}
  if e.class_name in LIGHT_CLASSES:
   extras['light']=light_info(e.class_name,v)
   # A static directional light exists only in the baked lightmaps.
   if extras['light']['type']=='directional' and extras['light']['mobility']=='static':extras['light']['enabled']=False
  elif e.class_name=='SkyLightComponent':extras['sky']=sky_info(p,v,mats)
  elif e.class_name=='ExponentialHeightFogComponent':extras['fog']=fog_info(v)
  elif e.class_name=='WindDirectionalSourceComponent':
   extras['wind']={'strength':v.get('Strength',.1),'speed':v.get('Speed',.1),'minGust':v.get('MinGustAmount',.1),'maxGust':v.get('MaxGustAmount',.2)}
  parent=v.get('AttachParent',{}).get('index',0)
  nodes.append({'id':base+idx,'parent':base+parent if parent>0 else 0,'name':e.object_name,'mesh':mesh,'materials':override,
   'position':v.get('RelativeLocation',{'x':0,'y':0,'z':0}),
   'rotation':v.get('RelativeRotation',{'pitch':0,'yaw':0,'roll':0}),
   'scale':v.get('RelativeScale3D',{'x':1,'y':1,'z':1}),
   **({'beam':beam} if beam else {}),**({'effect':effect} if effect else {}),**extras})


LIGHT_CLASSES={'DirectionalLightComponent':'directional','PointLightComponent':'point','SpotLightComponent':'spot'}
MOBILITY={'EComponentMobility::Static':'static','EComponentMobility::Stationary':'stationary','EComponentMobility::Movable':'movable'}

def srgb(c,default=(255,255,255)):
 """FColor property dict (sRGB bytes) -> [r, g, b] in 0..1."""
 return [round(c.get(k,d)/255,4) for k,d in zip('rgb',default)] if isinstance(c,dict) else [d/255 for d in default]

_ARCHETYPES={}

def archetype(p,e,resolve):
 """Properties of an export's template in its blueprint package (e.g. PointLight_GEN_VARIABLE
 in /Game/Stage/Common/PULSELIGHTSTAGE24_A), or {} for native templates."""
 i=e.template_index;chain=[]
 while i<0 and len(chain)<8:
  imp=p.imports[-i-1];chain.append(imp);i=imp.outer_index
 if len(chain)<3 or chain[-1].class_name!='Package' or not chain[-1].object_name.startswith('/Game/'):return {}
 key=(chain[-1].object_name,chain[0].object_name,chain[1].object_name)
 if key in _ARCHETYPES:return _ARCHETYPES[key]
 out={}
 try:
  data=resolve(game_path_to_virtual(chain[-1].object_name))
  if data:
   bp=Package(data,chain[-1].object_name)
   for x in bp.exports:
    if x.object_name==chain[0].object_name and x.outer_index>0 and bp.exports[x.outer_index-1].object_name==chain[1].object_name:
     out,_=properties(bp,bp.reader(x.serial_offset));break
 except Exception:out={}
 # Blueprint-internal references (AttachParent to its own scene root) mean nothing in the level.
 out={k:val for k,val in out.items() if k not in ('AttachParent','AttachSocketName')}
 _ARCHETYPES[key]=out
 return out

def light_info(cls,v):
 """A light as the original serialises it. Missing properties are UE 4.13 defaults (cooked
 packages omit them): intensity 5000 (point/spot) or 10 (directional), white, radius 1000 cm,
 inverse-squared falloff, cast shadows, stationary."""
 kind=LIGHT_CLASSES[cls]
 channel=v.get('LightChannel','')
 return {'type':kind,
  'intensity':v.get('Intensity',10.0 if kind=='directional' else 5000.0),
  'color':srgb(v.get('LightColor')),
  'radius':v.get('AttenuationRadius',1000.0)*.01,
  'inner':v.get('InnerConeAngle',0.0),'outer':v.get('OuterConeAngle',44.0),
  'inverseSquared':v.get('bUseInverseSquaredFalloff',True),
  'falloffExponent':v.get('LightFalloffExponent',8.0),
  'shadows':v.get('CastShadows',True),
  'channel':channel if isinstance(channel,str) else '',
  'mobility':MOBILITY.get(v.get('Mobility',''),'stationary'),
  'enabled':bool(v.get('bVisible',True) and v.get('bAffectsWorld',True)),
  'indirect':v.get('IndirectLightingIntensity',1.0)}

def sky_info(p,v,mats):
 cube=''
 ref=(v.get('Cubemap') or {}).get('object') if isinstance(v.get('Cubemap'),dict) else None
 specified=str(v.get('SourceType',''))=='SLS_SpecifiedCubemap'
 if ref and specified:cube=sky_cubemap(mats.resolve,ref.split('.')[0])
 return {'intensity':v.get('Intensity',1.0),'color':srgb(v.get('LightColor')),
  'lower':srgb(v.get('LowerHemisphereColor'),(0,0,0)),'lowerIsBlack':bool(v.get('bLowerHemisphereIsBlack',True)),
  'cubemap':cube,'specified':specified}

def fog_info(v):
 c=v.get('FogInscatteringColor')
 rgb=[c.get(k,.5) for k in 'rgb'] if isinstance(c,dict) and 'r' in c else [.447,.638,1.0]
 return {'density':v.get('FogDensity',.02),'heightFalloff':v.get('FogHeightFalloff',.2),'color':rgb,
  'maxOpacity':v.get('FogMaxOpacity',1.0),'startDistance':v.get('StartDistance',0.0)*.01,
  'enabled':bool(v.get('bVisible',True))}

POST_KEYS=('BloomIntensity','BloomThreshold','FilmSaturation','FilmContrast','VignetteIntensity','SceneColorTint',
 'AmbientOcclusionIntensity','AmbientOcclusionRadius','IndirectLightingIntensity','IndirectLightingColor',
 'AutoExposureMinBrightness','AutoExposureMaxBrightness','AutoExposureBias')

def post_volume(p,e,env):
 """Unbound, enabled post process volumes; the highest priority one is the stage's look
 (drama/demo volumes are disabled in the cooked maps)."""
 try:v,_=properties(p,p.reader(e.serial_offset))
 except Exception:return
 if not v.get('bUnbound',False) or not v.get('bEnabled',True):return
 settings=v.get('Settings') if isinstance(v.get('Settings'),dict) else {}
 picked={}
 for k in POST_KEYS:
  if settings.get('bOverride_'+k) and k in settings:
   val=settings[k]
   picked[k]=[val.get(c,0) for c in 'rgba'] if isinstance(val,dict) and 'r' in val else val
 prio=v.get('Priority',0.0)
 if 'post' not in env or prio>=env['post'].get('priority',-1e9):
  env['post']=dict(picked,priority=prio,name=e.object_name)

def sky_cubemap(resolve,path,size=128):
 """Skylight cubemap -> Assets/_Project/Stages/Lighting/<name>.t7sky (Unity cube faces, RGBAHalf,
 resampled to `size` for ambient light), the layout Tools/OriginalLighting/prepare_hdrp_sky.py
 writes for stg29."""
 import gzip
 name=re.sub(r'[^A-Za-z0-9_]+','_',path.rsplit('/',1)[-1])
 out=PROJECT/'Assets/_Project/Stages/Lighting'/(name+'.t7sky')
 if out.exists():return name
 try:
  data=resolve(game_path_to_virtual(path))
  pkg=Package(data,path);exp=next(x for x in pkg.exports if x.class_name=='TextureCube')
  tex=read_texture(pkg,exp)
  mip=tex.mips[0];n=mip.width
  if tex.pixel_format=='PF_FloatRGBA':
   src=np.frombuffer(mip.data,'<f2',6*n*n*4).reshape(6,n,n,4).astype(np.float32)
  else:
   strip=np.asarray(to_image(tex).convert('RGBA')).astype(np.float32)/255
   src=np.stack([strip[:,i*n:(i+1)*n] for i in range(6)]);src[...,:3]=src[...,:3]**2.2
  src=np.nan_to_num(src)
  m=size
  u,vv=np.meshgrid(2*(np.arange(m)+.5)/m-1,1-2*(np.arange(m)+.5)/m)
  one=np.ones_like(u);faces=[]
  for ux,uy,uz in [(one,-vv,-u),(-one,-vv,u),(u,one,vv),(u,-one,-vv),(u,-vv,one),(-u,-vv,-one)]:
   x,y,z=ux,-uz,uy
   xyz=np.stack([x,y,z]);major=np.argmax(np.abs(xyz),axis=0)
   axis=np.take_along_axis(xyz,major[None],axis=0)[0];face=major*2+(axis<0);den=np.abs(axis)
   sc=np.choose(face,[-z,z,x,x,x,-x])/den;tc=np.choose(face,[-y,-y,z,-z,-y,-y])/den
   ix=np.clip(((sc+1)*.5*n).astype(int),0,n-1);iy=np.clip(((tc+1)*.5*n).astype(int),0,n-1)
   px=src[face,iy,ix].copy();px[:,:,3]=1
   faces.append(px[::-1].astype('<f2').tobytes())
  out.parent.mkdir(parents=True,exist_ok=True)
  out.write_bytes(gzip.compress(b'T7SK'+struct.pack('<I',m)+b''.join(faces),mtime=0))
  return name
 except Exception as ex:
  print('sky cubemap failed',path,ex,flush=True);return ''

def god_ray(p,v,mats):
 """BP_LightBeam light shaft. Its mesh is a 1-unit line that M_GodRay_* widens in the vertex
 shader, so record the MaterialInstanceDynamic's parameters and build the beam in Unity."""
 out={'width':100.0,'length':1000.0,'brightness':1.0,'fadedistance':2048.0,'color':[1,1,1,1],'mask':''}
 for o in v.get('OverrideMaterials',[]) or []:
  i=o.get('index',0)
  if i<=0 or p.exports[i-1].class_name!='MaterialInstanceDynamic':continue
  props=read_properties(p.reader(p.exports[i-1].serial_offset))
  for tagname in ('ScalarParameterValues','VectorParameterValues','TextureParameterValues'):
   tag=props.get(tagname)
   if tag is None or getattr(tag,'inner_type','')!='StructProperty':continue
   for el in _struct_array(p,tag):
    name,val=(el.get('ParameterName') or '').lower(),el.get('ParameterValue')
    if tagname=='ScalarParameterValues' and isinstance(val,float):
     if name in ('width','length','brightness','fadedistance'):out[name]=val
    elif tagname=='VectorParameterValues' and hasattr(val,'value_offset') and val.size==16 and name=='color':
     out['color']=list(struct.unpack_from('<4f',p.data,val.value_offset))
    elif tagname=='TextureParameterValues' and isinstance(val,int) and val<0:
     tp=import_path(p,val)
     if tp:out['mask']=mats.texture(tp,False)
 return out


def main():
 only=set(sys.argv[1:])
 OUT.mkdir(parents=True,exist_ok=True);TEXTURES.mkdir(parents=True,exist_ok=True)
 source=Source(str(ROOT/'TEKKEN.7'),TEKKEN_ASSET_KEY);resolve=_resolver(source)
 inventory={i.path.lower():i for i in source.items()}
 mats=Materials(resolve);cache={};meshes={};stages=[];failures=[]
 for path,item in sorted(inventory.items()):
  match=re.search(r'/maps/(stg\d{2}[a-z]?)_geom\.umap$',path)
  if not match or (only and match[1] not in only):continue
  stage=match[1];nodes=[];starts=[];env={}
  # _reload holds what each round resets (breakable floors, fences, pillars) and _demo pieces that
  # stay for the fight (stg08a's ring); both are part of the playable stage.
  for level,lpath in enumerate([path]+[path.replace('_geom.umap',x+'.umap') for x in SUBLEVELS]):
   if lpath not in inventory:continue
   p=Package(inventory[lpath].read(),inventory[lpath].path)
   collect(stage,level,p,nodes,starts if level==0 else [],cache,meshes,mats,inventory,failures,env)
  p=Package(item.read(),item.path)
  count=sum(bool(n['mesh']) for n in nodes)
  # Where a match begins: the first floor's GAME_START capsule (multi-floor stages have one per floor).
  start=None
  for name,root in sorted(starts,key=lambda s:(int((re.search(r'Start(\d)',s[0]) or [0,'0'])[1]),s[0])):
   if root>0:
    try:
     # A marker at the map origin has no RelativeLocation (cooked defaults are omitted).
     c,_=properties(p,p.reader(p.exports[root-1].serial_offset));l=c.get('RelativeLocation',{'x':0,'y':0,'z':0})
     start={'x':l['x']*.01,'y':l['z']*.01,'z':-l['y']*.01,'yaw':c.get('RelativeRotation',{}).get('yaw',0)};break
    except Exception:pass
  if count:stages.append({'name':stage,'nodes':nodes,'hasStart':start is not None,'start':{k:v for k,v in (start or {}).items() if k!='yaw'} or {'x':0,'y':0,'z':0},
   'startYaw':(start or {}).get('yaw',-90),'post':env.get('post',{})})
  print(stage,count,'placed meshes',len(mats.entries),'materials',sum(1 for t in mats.textures.values() if t),'textures',flush=True)
 materials=list(mats.entries.values())
 (OUT/'stages.json').write_text(json.dumps({'stages':stages,'meshes':list(meshes.values()),'materials':materials}),encoding='utf-8')
 # Which materials each stage actually draws, so the report can name the stages affected.
 used={}
 for st in stages:
  for n in st['nodes']:
   if not n['mesh']:continue
   slots=list(meshes[n['mesh']]['materials'])
   for i,o in enumerate(n['materials']):
    if o and i<len(slots):slots[i]=o
   for s in slots:used.setdefault(s,set()).add(st['name'])
 untextured=[{'material':m['path'],'stages':sorted(used.get(m['key'],())),'reason':m['note']} for m in materials if not m['base'] and not m['hidden'] and m['key'] in used]
 report={'stages':len(stages),'uniqueMeshes':sum(bool(x) for x in cache.values()),'failures':failures,
  'materials':len([m for m in materials if m['key'] in used]),'texturedMaterials':len([m for m in materials if m['base'] and m['key'] in used]),
  'textures':sum(1 for t in mats.textures.values() if t),'textureFailures':sorted(mats.texture_failures.values()),
  'untexturedMaterials':untextured,
  'notes':'Base colour, normal and emissive maps from each MaterialInstance chain become HDRP/Lit maps. Shader graphs (layer blends, vertex-colour mixes, water, baked lightmaps) are not converted.'}
 (OUT/'report.json').write_text(json.dumps(report,indent=1),encoding='utf-8')
 print('DONE',report['stages'],report['uniqueMeshes'],'meshes',report['texturedMaterials'],'/',report['materials'],'materials textured, failures',len(failures),flush=True)
if __name__=='__main__':main()
