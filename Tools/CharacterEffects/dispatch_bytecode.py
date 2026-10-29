"""Bounded disassembly of the observed pre-FProperty Tekken UE4 Kismet layout.

Original package functions are re-read through t7extract.scripts.decode_function.
No recovery is claimed for native executable functions. Unknown tokens fail closed.
Opcode/layout reference: https://github.com/atenfyr/UAssetAPI/tree/master/UAssetAPI/Kismet/Bytecode
"""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path
import struct
import sys

NAMES = {
0x00:'LocalVariable',0x01:'InstanceVariable',0x02:'DefaultVariable',0x04:'Return',
0x06:'Jump',0x07:'JumpIfNot',0x0B:'Nothing',0x0F:'Let',0x12:'ClassContext',
0x13:'MetaCast',0x14:'LetBool',0x15:'EndParmValue',0x16:'EndFunctionParms',
0x17:'Self',0x18:'Skip',0x19:'Context',0x1A:'Context_FailSilent',
0x1B:'VirtualFunction',0x1C:'FinalFunction',0x1D:'IntConst',0x1E:'FloatConst',
0x1F:'StringConst',0x20:'ObjectConst',0x21:'NameConst',0x22:'RotationConst',
0x23:'VectorConst',0x24:'ByteConst',0x25:'IntZero',0x26:'IntOne',0x27:'True',
0x28:'False',0x2A:'NoObject',0x2B:'TransformConst',0x2C:'IntConstByte',
0x2D:'NoInterface',0x2E:'DynamicCast',0x2F:'StructConst',0x30:'EndStructConst',
0x31:'SetArray',0x32:'EndArray',0x33:'PropertyConst',0x34:'UnicodeStringConst',
0x35:'Int64Const',0x36:'UInt64Const',0x38:'PrimitiveCast',0x42:'StructMemberContext',
0x43:'LetMulticastDelegate',0x44:'LetDelegate',0x45:'LocalVirtualFunction',
0x46:'LocalFinalFunction',0x48:'LocalOutVariable',0x4B:'InstanceDelegate',
0x4C:'PushExecutionFlow',0x4D:'PopExecutionFlow',0x4E:'ComputedJump',
0x4F:'PopExecutionFlowIfNot',0x51:'InterfaceContext',0x52:'ObjToInterfaceCast',
0x53:'EndOfScript',0x54:'CrossInterfaceCast',0x55:'InterfaceToObjCast',
0x5B:'SkipOffsetConst',0x5C:'AddMulticastDelegate',0x5D:'ClearMulticastDelegate',
0x5F:'LetObj',0x60:'LetWeakObjPtr',0x61:'BindDelegate',0x62:'RemoveMulticastDelegate',
0x63:'CallMulticastDelegate',0x64:'LetValueOnPersistentFrame',
0x65:'ArrayConst',0x66:'EndArrayConst',0x68:'CallMath',0x69:'SwitchValue',0x6B:'ArrayGetByRef'}

class Decoder:
    def __init__(self, package, code):
        self.package, self.code, self.pos, self.mem = package, code, 0, 0
        self.nodes=[]

    def read(self, fmt):
        n=struct.calcsize(fmt)
        if self.pos+n>len(self.code): raise ValueError('Bytecode exceeds declared span')
        result=struct.unpack_from(fmt,self.code,self.pos)
        self.pos+=n; self.mem+=n
        return result[0] if len(result)==1 else list(result)

    def obj(self):
        i=self.read('<i'); self.mem+=4
        if not -len(self.package.imports)<=i<=len(self.package.exports): raise ValueError('Invalid object index')
        def path(index,seen=None):
            if not index:return None
            seen=set() if seen is None else seen
            if index in seen:raise ValueError('Cyclic outer')
            seen.add(index)
            o=self.package.exports[index-1] if index>0 else self.package.imports[-index-1]
            parent=path(o.outer_index,seen)
            return (parent+'.' if parent else '')+o.object_name
        return {'index':i,'path':path(i)}

    def fname(self):
        i,n=self.read('<ii')
        self.mem+=4 # Script FName occupies 12 bytes in the VM.
        if not 0<=i<len(self.package.names): raise ValueError('Invalid name index')
        return self.package.names[i]+('_'+str(n-1) if n else '')

    def until(self,term):
        result=[]
        while True:
            node=self.expr()
            if node['token']==NAMES[term]:return result
            result.append(node)

    def expr(self):
        pos,mem=self.pos,self.mem
        op=self.read('<B')
        if op not in NAMES:raise ValueError(f'Unsupported token 0x{op:02X} at serialized {pos}/memory {mem}')
        node={'offset':pos,'memoryOffset':mem,'token':NAMES[op]}
        self.nodes.append(node)
        if op in (0,1,2,0x20,0x33,0x48):node['object']=self.obj()
        elif op in (0x06,0x4C,0x5B):node['target']=self.read('<I')
        elif op==0x07:node.update(target=self.read('<I'),condition=self.expr())
        elif op in (0x04,0x4E,0x4F,0x51,0x5D):node['expression']=self.expr()
        elif op==0x0F:node.update(property=self.obj(),destination=self.expr(),value=self.expr())
        elif op in (0x14,0x43,0x44,0x5C,0x5F,0x60,0x62,0x6B):node.update(left=self.expr(),right=self.expr())
        elif op==0x64:node.update(destination=self.obj(),value=self.expr())
        elif op in (0x1B,0x45):node.update(function=self.fname(),args=self.until(0x16))
        elif op in (0x1C,0x46,0x68):node.update(function=self.obj(),args=self.until(0x16))
        elif op in (0x19,0x1A,0x12):
            node.update(context=self.expr(),skipBytes=self.read('<I'),rvalue=self.obj(),expression=self.expr())
        elif op in (0x13,0x2E,0x52,0x54,0x55):node.update(castClass=self.obj(),expression=self.expr())
        elif op==0x42:node.update(member=self.obj(),context=self.expr())
        elif op==0x18:node.update(skipBytes=self.read('<I'),expression=self.expr())
        elif op==0x1D:node['value']=self.read('<i')
        elif op==0x1E:node['value']=self.read('<f')
        elif op in (0x24,0x2C):node['value']=self.read('<B')
        elif op in (0x21,0x4B):node['value']=self.fname()
        elif op in (0x22,0x23):node['value']=self.read('<3f')
        elif op==0x2B:node['value']=self.read('<10f')
        elif op in (0x1F,0x34):
            values=[]
            while True:
                value=self.read('<B' if op==0x1F else '<H')
                if not value:break
                values.append(value)
            node['value']=''.join(map(chr,values))
        elif op in (0x35,0x36):node['value']=self.read('<q' if op==0x35 else '<Q')
        elif op==0x2F:node.update(struct=self.obj(),structSize=self.read('<i'),items=self.until(0x30))
        elif op==0x31:node.update(destination=self.expr(),items=self.until(0x32))
        elif op==0x38:node.update(castType=self.read('<B'),expression=self.expr())
        elif op==0x61:node.update(function=self.fname(),delegate=self.expr(),object=self.expr())
        elif op==0x63:node.update(signature=self.obj(),delegate=self.expr(),args=self.until(0x16))
        elif op==0x65:node.update(inner=self.obj(),count=self.read('<i'),items=self.until(0x66))
        elif op==0x69:
            count=self.read('<H'); node.update(endTarget=self.read('<I'),index=self.expr())
            node['cases']=[{'index':self.expr(),'nextTarget':self.read('<I'),'expression':self.expr()} for _ in range(count)]
            node['default']=self.expr()
        elif op not in (0x0B,0x15,0x16,0x17,0x25,0x26,0x27,0x28,0x2A,0x2D,0x30,0x32,0x4D,0x53,0x66):
            raise ValueError(f'Missing handler {node}')
        node.update(endOffset=self.pos,endMemoryOffset=self.mem)
        return node

    def decode(self):
        roots=[]
        while self.pos<len(self.code):roots.append(self.expr())
        starts={r['memoryOffset'] for r in roots}
        for n in self.nodes:
            if n['token'] in ('Jump','JumpIfNot','PushExecutionFlow') and n['target'] not in starts:
                raise ValueError(f'Jump target {n["target"]} is not a statement boundary')
        return roots

def compact(n):
    if not isinstance(n,dict):return str(n)
    if 'path' in n:return n['path'] or 'null'
    t=n.get('token','')
    if t.endswith('Variable'):return compact(n['object'])
    if t in ('ObjectConst','PropertyConst'):return compact(n['object'])
    if 'function' in n and 'args' in n:return compact(n['function'])+'('+', '.join(map(compact,n['args']))+')'
    if t in ('Context','Context_FailSilent','ClassContext'):return compact(n['context'])+'::'+compact(n['expression'])
    if t=='StructMemberContext':return compact(n['context'])+'.'+compact(n['member']).split('.')[-1]
    if t in ('Let','LetValueOnPersistentFrame'):return compact(n['destination'])+' = '+compact(n['value'])
    if t.startswith('Let'):return compact(n['left'])+' = '+compact(n['right'])
    if 'value' in n:return repr(n['value'])
    if 'target' in n:return t+' '+str(n['target'])+(' unless '+compact(n['condition']) if 'condition' in n else '')
    if 'expression' in n:return t+'('+compact(n['expression'])+')'
    if t=='SetArray':return compact(n['destination'])+' = ['+', '.join(map(compact,n['items']))+']'
    if t=='StructConst':return compact(n['struct'])+'{'+', '.join(map(compact,n['items']))+'}'
    return t

def main():
    ap=argparse.ArgumentParser()
    ap.add_argument('--package',required=True,type=Path)
    ap.add_argument('--t7extract',required=True,type=Path)
    ap.add_argument('--out',required=True,type=Path)
    args=ap.parse_args()
    sys.path.insert(0,str(args.t7extract))
    from t7extract.package import Package
    from t7extract.scripts import decode_function
    data=args.package.read_bytes(); package=Package(data,str(args.package))
    result={'package':str(args.package),'sha256':hashlib.sha256(data).hexdigest(),
            'references':['https://github.com/atenfyr/UAssetAPI/tree/master/UAssetAPI/Kismet/Bytecode'],
            'limitations':['Observed pre-FProperty serialized layout only; native calls remain opaque.','Memory offsets account for 32-bit serialized indices expanded to 64-bit pointers and serialized 8-byte names expanded to 12 VM bytes.'],
            'functions':[]}
    for i,exp in enumerate(package.exports,1):
        if exp.class_name!='Function':continue
        info,code=decode_function(package,exp,package.export_data(exp))
        dec=Decoder(package,code)
        row={'index':i,'name':exp.object_name,'info':info}
        try:
            row['statements']=dec.decode()
            if dec.mem!=info['script_memory_size']:raise ValueError(f'Memory length mismatch {dec.mem} != {info["script_memory_size"]}')
            row['complete']=True
        except Exception as e:
            row.update(complete=False,error=str(e),decodedUntil=dec.pos)
        result['functions'].append(row)
    args.out.parent.mkdir(parents=True,exist_ok=True)
    args.out.write_text(json.dumps(result,indent=2)+'\n',encoding='utf-8')
    lines=[]
    for f in result['functions']:
        lines.append(f'FUNCTION {f["name"]} complete={f["complete"]} bytecodePackageOffset={f["info"]["bytecode_package_offset"]}')
        for n in f.get('statements',[]):lines.append(f'  mem={n["memoryOffset"]:04d} serialized={n["offset"]:04d} '+compact(n))
        if not f['complete']:lines.append('  ERROR '+f['error'])
    args.out.with_suffix('.txt').write_text('\n'.join(lines)+'\n',encoding='utf-8')
    print(json.dumps({'out':str(args.out),'functions':len(result['functions']),'complete':sum(x['complete'] for x in result['functions']), 'errors':[(x['name'],x.get('error')) for x in result['functions'] if not x['complete']]}))

if __name__=='__main__':main()
