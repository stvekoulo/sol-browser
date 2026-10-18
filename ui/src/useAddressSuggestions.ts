import { useEffect,useRef,useState,type RefObject,type KeyboardEvent } from 'react';
import { displayAddress,onMessage,send } from './bridge';
type Choice={url:string;value:string};
export function useAddressSuggestions(id:string|undefined,setText:(value:string)=>void,input:RefObject<HTMLInputElement|null>){
  const choices=useRef<Choice[]>([]),selected=useRef(-1),typed=useRef(''),[count,setCount]=useState(0);
  useEffect(()=>onMessage(data=>{
    const message=data as {event?:string;id?:string;items?:Choice[]};if(message.event!=='suggestions'||message.id!==id)return;
    choices.current=message.items??[];selected.current=-1;setCount(choices.current.length);
  }),[id]);
  const remember=(value:string)=>{typed.current=value;selected.current=-1};
  const changed=(value:string)=>{remember(value);setText(value)};
  const clear=()=>{choices.current=[];selected.current=-1;setCount(0)};
  const key=(event:KeyboardEvent<HTMLInputElement>)=>{
    if(event.nativeEvent.isComposing||event.altKey||event.ctrlKey||event.metaKey||event.shiftKey||!choices.current.length)return false;
    if(event.key!=='ArrowDown'&&event.key!=='ArrowUp'&&!(event.key==='Tab'&&selected.current>=0))return false;
    event.preventDefault();
    if(event.key==='Tab'){selected.current=-1;send({type:'suggest.select',index:-1});return true;}
    const next=((selected.current+1+(event.key==='ArrowDown'?1:-1)+choices.current.length+1)%(choices.current.length+1))-1;
    selected.current=next;const value=next<0?typed.current:displayAddress(choices.current[next].value);setText(value);
    requestAnimationFrame(()=>input.current?.setSelectionRange(value.length,value.length));send({type:'suggest.select',index:next});return true;
  };
  return {changed,remember,clear,key,count};
}
