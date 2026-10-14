// Adapted from beUI animated-sidebar, Copyright (c) 2026 Saurabh Chauhan, MIT.
// The native host owns the width and placement; animate only its panel content.
import {motion,useAnimationControls,useReducedMotion} from 'motion/react';
import {useEffect,type ReactNode} from 'react';
import {EASE_DRAWER} from '@/lib/ease';
export function AnimatedSidebarPanel({open,run,onSettled,children,...props}:{open:boolean;run:number;onSettled:(run:number)=>void;children:ReactNode;className?:string;'aria-label'?:string}){
  const controls=useAnimationControls(),reduce=useReducedMotion();
  useEffect(()=>{
    if(!run)return;
    let alive=true;
    const reduced=window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    controls.start({opacity:open?1:0,x:open?0:reduced?0:-18,transition:{duration:reduced?0.08:open?0.28:0.22,ease:EASE_DRAWER}}).then(()=>{if(alive)onSettled(run)});
    return()=>{alive=false;controls.stop()};
  },[open,run,reduce,controls,onSettled]);
  return <motion.aside {...props} data-slot="animated-sidebar" data-state={open?'expanded':'collapsed'} initial={false} animate={controls} style={{willChange:run?'transform, opacity':undefined}}>{children}</motion.aside>;
}
