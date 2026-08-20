#include "hpd_history_v1.h"
#include <stdint.h>
#include <string.h>
static hpd_error_v1 error_value(void){hpd_error_v1 e={0};e.abi_size=32;e.abi_version=0x00010000;return e;}
static hpd_bytes_v1 input_value(uint8_t*p,uint8_t kind){uint32_t i;p[0]=0xa5;p[1]=1;p[2]=1;p[3]=2;p[4]=kind;p[5]=3;p[6]=0x58;p[7]=0x20;for(i=0;i<32;i++)p[8+i]=(uint8_t)(i+1);p[40]=4;p[41]=1;p[42]=5;p[43]=0x43;p[44]=0xa1;p[45]=1;p[46]=1;hpd_bytes_v1 b={0};b.abi_size=24;b.abi_version=0x00010000;b.ptr=p;b.len=47;return b;}
static hpd_output_v1 output_value(uint8_t*p,uint32_t n){hpd_output_v1 o={0};o.abi_size=40;o.abi_version=0x00010000;o.ptr=p;o.capacity=n;return o;}
int main(void){
  const uint8_t payload[]={0xa1,0x01,0x01};uint8_t request[47]={0},out_bytes[128]={0};hpd_handle_v1 h=0,c=0;hpd_error_v1 e=error_value();hpd_bytes_v1 in=input_value(request,1);hpd_output_v1 out=output_value(out_bytes,sizeof out_bytes);
  if(sizeof(hpd_bytes_v1)!=24||sizeof(hpd_output_v1)!=40||sizeof(hpd_error_v1)!=32||hpd_history_abi_version()!=0x00010000)return 1;
  if(hpd_history_query_open(&in,&h,&e)||!h)return 2;
  if(hpd_history_query_next(h,&out,&e)||out.written!=sizeof payload||memcmp(out_bytes,payload,sizeof payload))return 3;
  if(hpd_history_query_next(h,&out,&e)!=1)return 4;
  if(hpd_history_query_close(h,&e)||hpd_history_query_next(h,&out,&e)!=16)return 5;
  in=input_value(request,2);if(hpd_history_subscription_open(&in,&h,&e)||hpd_history_subscription_next(h,1,&out,&e)||hpd_history_subscription_ack(h,out.cursor,&e)||hpd_history_subscription_close(h,&e))return 6;
  in=input_value(request,3);if(hpd_history_export_start(&in,&h,&e)||hpd_history_export_status(h,&out,&e)||out.written!=80)return 7;
  if(hpd_history_export_content_open(h,&c,&e)||hpd_history_export_content_next(c,&out,&e)||hpd_history_export_content_close(c,&e)||hpd_history_export_cancel(h,&e))return 8;
  in=input_value(request,5);if(hpd_privacy_delete_start(&in,&h,&e)||hpd_privacy_status(h,&out,&e)||hpd_privacy_cancel(h,&e))return 9;
  in=input_value(request,6);if(hpd_privacy_hold_start(&in,&h,&e)||hpd_privacy_hold_release(h,&in,&e))return 10;
  hpd_error_free(&e);return 0;
}
