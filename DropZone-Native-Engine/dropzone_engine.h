#ifndef DROPZONE_ENGINE_H
#define DROPZONE_ENGINE_H

#include <stdint.h>
#include <stddef.h>

#ifdef __cplusplus
extern "C" {
#endif

/* DROPZONE DZP1 Engine Event Types */
#define DZ_EVENT_SERVER_STARTED           1
#define DZ_EVENT_PEER_DISCOVERED          2
#define DZ_EVENT_TRANSFER_REQUEST         10
#define DZ_EVENT_TRANSFER_ACCEPTED        11
#define DZ_EVENT_TRANSFER_REJECTED        12
#define DZ_EVENT_PROGRESS                 20
#define DZ_EVENT_TELEMETRY                25
#define DZ_EVENT_PAUSED                   30
#define DZ_EVENT_RESUMED                  31
#define DZ_EVENT_CANCELLED                32
#define DZ_EVENT_COMPLETED                40
#define DZ_EVENT_PAIRING_REQUEST          50
#define DZ_EVENT_PAIRING_PIN_DISPLAY      51
#define DZ_EVENT_PAIRING_COMPLETED        52
#define DZ_EVENT_PAIRING_FAILED           53
#define DZ_EVENT_NAT_CANDIDATE_DISCOVERED 60
#define DZ_EVENT_ERROR                    99

/* Engine Callback Signature */
typedef void (*dz_event_callback_t)(int32_t event_type, const char* payload_json, void* user_data);

/* Lifecycle Functions */
int32_t dropzone_engine_init(const char* config_json);
void dropzone_engine_set_event_callback(dz_event_callback_t callback, void* user_data);
int32_t dropzone_engine_start_server(const char* bind_ip, uint16_t port);
int32_t dropzone_engine_stop_server(void);
int32_t dropzone_engine_shutdown(void);
const char* dropzone_engine_get_last_error(void);

/* Discovery Functions */
int32_t dropzone_engine_start_discovery(const char* service_type, uint16_t port, const char* txt_json);
int32_t dropzone_engine_stop_discovery(void);

/* Transfer Pipeline Functions */
int32_t dropzone_engine_send_job(const char* target_ip, uint16_t target_port, const char* job_spec_json);
int32_t dropzone_engine_accept_job(const char* job_id, const char* save_directory);
int32_t dropzone_engine_reject_job(const char* job_id, const char* reason);
int32_t dropzone_engine_pause_job(const char* job_id);
int32_t dropzone_engine_resume_job(const char* job_id);
int32_t dropzone_engine_cancel_job(const char* job_id);

/* Cryptography and Pairing Functions */
int32_t dropzone_engine_generate_identity_key(char* out_public_key_hex, size_t max_len);
int32_t dropzone_engine_start_pairing(const char* target_ip, uint16_t target_port, const char* device_id, const char* device_name);
int32_t dropzone_engine_confirm_pairing(const char* session_id, int32_t accepted);

/* NAT Traversal and Diagnostics */
int32_t dropzone_engine_test_stun(const char* stun_server, char* out_reflexive_address, size_t max_len);
int32_t dropzone_engine_gather_nat_candidates(const char* stun_server, const char* turn_server, const char* turn_user, const char* turn_pass);
int32_t dropzone_engine_ping_peer(const char* peer_ip, uint16_t peer_port, double* out_latency_ms);

#ifdef __cplusplus
}
#endif

#endif /* DROPZONE_ENGINE_H */
