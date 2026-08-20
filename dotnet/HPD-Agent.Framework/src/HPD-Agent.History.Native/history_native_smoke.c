#include "hpd_history_v1.h"
#include <stdint.h>
#include <string.h>
static hpd_error_v1 error_value(void){hpd_error_v1 e={0};e.abi_size=32;e.abi_version=0x00010000;return e;}
static hpd_bytes_v1 input_value(const uint8_t*p,uint32_t n){hpd_bytes_v1 b={0};b.abi_size=24;b.abi_version=0x00010000;b.ptr=p;b.len=n;return b;}
static hpd_output_v1 output_value(uint8_t*p,uint32_t n){hpd_output_v1 o={0};o.abi_size=40;o.abi_version=0x00010000;o.ptr=p;o.capacity=n;return o;}
int main(void){
  const uint8_t req[]={0xa1,0x01,0x01};uint8_t out_bytes[128]={0};hpd_handle_v1 h=0,c=0;hpd_error_v1 e=error_value();hpd_bytes_v1 in=input_value(req,sizeof req);hpd_output_v1 out=output_value(out_bytes,sizeof out_bytes);
  if(sizeof(hpd_bytes_v1)!=24||sizeof(hpd_output_v1)!=40||sizeof(hpd_error_v1)!=32||hpd_history_abi_version()!=0x00010000)return 1;
  if(hpd_history_query_open(&in,&h,&e)||!h)return 2;if(hpd_history_query_next(h,&out,&e)||out.written!=sizeof req||memcmp(out_bytes,req,sizeof req))return 3;if(hpd_history_query_next(h,&out,&e)!=1)return 4;if(hpd_history_query_close(h,&e)||hpd_history_query_next(h,&out,&e)!=16)return 5;
  if(hpd_history_subscription_open(&in,&h,&e)||hpd_history_subscription_next(h,1,&out,&e)||hpd_history_subscription_ack(h,out.cursor,&e)||hpd_history_subscription_close(h,&e))return 6;
  if(hpd_history_export_start(&in,&h,&e)||hpd_history_export_status(h,&out,&e)||out.written!=80)return 7;if(hpd_history_export_content_open(h,&c,&e)||hpd_history_export_content_next(c,&out,&e)||hpd_history_export_content_close(c,&e)||hpd_history_export_cancel(h,&e))return 8;
  if(hpd_privacy_delete_start(&in,&h,&e)||hpd_privacy_status(h,&out,&e)||hpd_privacy_cancel(h,&e))return 9;if(hpd_privacy_hold_start(&in,&h,&e)||hpd_privacy_hold_release(h,&in,&e))return 10;
  hpd_error_free(&e);return 0;
}
