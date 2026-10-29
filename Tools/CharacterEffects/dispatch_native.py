"""Read-only PE string/code xrefs for original effect dispatch investigation."""
from pathlib import Path
import argparse,bisect,hashlib,json,re,struct
from capstone import Cs,CS_ARCH_X86,CS_MODE_64

class Image:
    def __init__(self,path):
        self.data=Path(path).read_bytes();b=self.data
        p=struct.unpack_from('<I',b,60)[0];n=struct.unpack_from('<H',b,p+6)[0];opt=struct.unpack_from('<H',b,p+20)[0]
        self.base=struct.unpack_from('<Q',b,p+48)[0]
        self.sections=[struct.unpack_from('<8sIIII',b,p+24+opt+40*i) for i in range(n)]
        pdata=next(s for s in self.sections if s[0].startswith(b'.pdata'))
        self.functions=[struct.unpack_from('<III',b,k) for k in range(pdata[4],pdata[4]+pdata[1]-11,12)]
        self.starts=[f[0] for f in self.functions]
        self.dis=Cs(CS_ARCH_X86,CS_MODE_64)
    def va(self,off):
        for name,vs,rva,size,raw in self.sections:
            if raw<=off<raw+size:return self.base+rva+off-raw
        raise ValueError('unmapped offset')
    def offset(self,va):
        for name,vs,rva,size,raw in self.sections:
            if self.base+rva<=va<self.base+rva+size:return raw+va-self.base-rva
        raise ValueError('unmapped address')
    def function(self,va):
        idx=bisect.bisect_right(self.starts,va-self.base)-1
        f=self.functions[idx]
        if not f[0]<=va-self.base<f[1]:return None
        def root(entry):
            for _ in range(30):
                off=self.offset(self.base+entry[2]);flags,_,count,_=self.data[off:off+4]
                if not flags&32:return entry[0]
                entry=struct.unpack_from('<III',self.data,off+4+((count+1)&~1)*2)
            raise ValueError('Unwind chain depth')
        start=root(f);end=f[1]
        for following in self.functions[idx+1:idx+100]:
            if root(following)!=start:break
            end=following[1]
        return self.base+start,self.base+end
    def instructions(self,start,end):
        off=self.offset(start)
        return [{'va':hex(a),'bytes':self.data[self.offset(a):self.offset(a)+n].hex(),'op':op,'args':args} for a,n,op,args in self.dis.disasm_lite(self.data[off:off+end-start],start)]
    def string_refs(self,name):
        targets={self.va(m.start()) for m in re.finditer(re.escape(name.encode('utf-16le')+b'\0\0'),self.data)}
        refs=[]
        text=next(s for s in self.sections if s[0].startswith(b'.text'))
        buf=self.data[text[4]:text[4]+text[3]]
        for m in re.finditer(b'[\x48\x4c]\x8d[\x05\x0d\x15\x1d\x25\x2d\x35\x3d]....',buf,re.S):
            off=m.start()+text[4];va=self.va(off);target=va+7+struct.unpack_from('<i',self.data,off+3)[0]
            if target in targets:
                bounds=self.function(va)
                refs.append({'stringVA':hex(target),'xrefVA':hex(va),'functionBounds':list(map(hex,bounds)) if bounds else None,
                             'instructions':self.instructions(*bounds) if bounds else self.instructions(va,va+96)})
        return refs
    def calls(self,target):
        refs=[];text=next(s for s in self.sections if s[0].startswith(b'.text'));buf=self.data[text[4]:text[4]+text[3]]
        for m in re.finditer(b'\xe8....',buf,re.S):
            off=m.start()+text[4];va=self.va(off);dest=va+5+struct.unpack_from('<i',self.data,off+1)[0]
            if dest==target:
                bounds=self.function(va)
                refs.append({'callVA':hex(va),'functionBounds':list(map(hex,bounds)) if bounds else None,
                             'instructions':self.instructions(*bounds) if bounds else self.instructions(va,va+96)})
        return refs
    def rip_refs(self,target):
        refs=[];text=next(s for s in self.sections if s[0].startswith(b'.text'));buf=self.data[text[4]:text[4]+text[3]]
        for m in re.finditer(b'[\x48\x4c][\x8b\x89\x8d][\x05\x0d\x15\x1d\x25\x2d\x35\x3d]....',buf,re.S):
            off=m.start()+text[4];va=self.va(off);dest=va+7+struct.unpack_from('<i',self.data,off+3)[0]
            if dest==target:
                bounds=self.function(va)
                refs.append({'xrefVA':hex(va),'functionBounds':list(map(hex,bounds)) if bounds else None,
                             'instructions':self.instructions(*bounds) if bounds else self.instructions(va,va+96)})
        return refs

def main():
    ap=argparse.ArgumentParser();ap.add_argument('--exe',type=Path,required=True);ap.add_argument('--out',type=Path,required=True)
    ap.add_argument('--string',action='append',default=[]);ap.add_argument('--call',action='append',default=[])
    ap.add_argument('--rip',action='append',default=[])
    args=ap.parse_args();im=Image(args.exe)
    result={'executable':str(args.exe),'sha256':hashlib.sha256(im.data).hexdigest(),'base':hex(im.base),
            'limitations':['Static instruction xrefs are leads, not proof of runtime execution.'],'stringRefs':{s:im.string_refs(s) for s in args.string},
            'callRefs':{s:im.calls(int(s,0)) for s in args.call},'ripRefs':{s:im.rip_refs(int(s,0)) for s in args.rip}}
    args.out.parent.mkdir(parents=True,exist_ok=True);args.out.write_text(json.dumps(result,indent=2)+'\n')
    print(json.dumps({'out':str(args.out),'strings':{k:len(v) for k,v in result['stringRefs'].items()},'calls':{k:len(v) for k,v in result['callRefs'].items()}}))
if __name__=='__main__':main()
