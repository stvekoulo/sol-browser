import {useEffect,useLayoutEffect,useRef,useState} from 'react';
import {motion,useReducedMotion} from 'motion/react';
import {interfaceScale,onMessage,send,type HostEvent} from './bridge';
import {EASE_DRAWER} from '@/lib/ease';
import App from './App';
import Sidebar from './Sidebar';
import {sidebarTheme} from './interfaceTheme';

// beUI animated-sidebar, MIT: desktop offcanvas spring and panel transition.
const SIDEBAR_MORPH_TRANSITION={type:'spring',stiffness:380,damping:35,mass:0.75} as const;
const PANEL_TRANSITION={duration:0.36,ease:EASE_DRAWER};

export default function Workspace(){
  const [sidebar,setSidebar]=useState<{open:boolean;width:number}|null>(null);
  const [scale,setScale]=useState(interfaceScale);
  const reduce=useReducedMotion(),slot=useRef<HTMLDivElement>(null);
  useEffect(()=>{
    const off=onMessage(data=>{
      const event=data as HostEvent;
      if(event.event==='sidebar')setSidebar(current=>({open:event.open??current?.open??true,width:event.width??current?.width??296}));
    });
    const rescale=()=>setScale(interfaceScale());
    window.addEventListener('ui-scale',rescale);
    send({type:'ui.ready'});
    return()=>{off();window.removeEventListener('ui-scale',rescale)};
  },[]);
  const ready=sidebar!==null;
  useEffect(()=>{if(ready)send({type:'ui.ready'})},[ready]);
  useLayoutEffect(()=>{
    const element=slot.current;
    if(!element)return;
    let frame=0;
    const publish=()=>{
      frame=0;
      const {x,y,width,height}=element.getBoundingClientRect();
      send({type:'ui.layout',x,y,width,height});
    };
    const observer=new ResizeObserver(()=>{if(!frame)frame=requestAnimationFrame(publish)});
    observer.observe(element);
    window.addEventListener('resize',publish);
    return()=>{observer.disconnect();cancelAnimationFrame(frame);window.removeEventListener('resize',publish)};
  },[ready]);
  if(!sidebar)return null;
  const width=sidebar.width/scale;
  return <div className="workspace" style={{'--sidebar-width':`${width}px`} as React.CSSProperties}>
    <motion.aside ref={sidebarTheme} data-surface-theme="light" data-slot="sidebar" data-state={sidebar.open?'expanded':'collapsed'} className="workspace-sidebar"
      initial={false} animate={{width:sidebar.open?width:0}} transition={reduce?{duration:0}:SIDEBAR_MORPH_TRANSITION}
      inert={!sidebar.open} aria-hidden={!sidebar.open}>
      <motion.div className="workspace-sidebar-content" initial={false}
        animate={{opacity:sidebar.open?1:0,x:sidebar.open?'0%':'-100%'}} transition={reduce?{duration:0}:PANEL_TRANSITION}>
        <Sidebar/>
      </motion.div>
    </motion.aside>
    <div className="workspace-right" data-slot="sidebar-inset">
      <App sidebarOpen={sidebar.open}/>
      <div ref={slot} className="workspace-page" aria-hidden="true"/>
    </div>
  </div>;
}
