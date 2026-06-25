/* Copyright (c) 2026 marcschier. Licensed under the MIT License. */

/*
 * Apples-to-apples native-C MQTT publisher for the cross-language throughput harness, using the
 * Eclipse Paho C synchronous MQTTClient v5 API: one persistent connection, a tight publish loop,
 * and MQTTClient_waitForCompletion per message for QoS > 0 — matching exactly what the .NET
 * publishers (Mqtt.Client.PublishAsync / MQTTnet) do.
 *
 *   paho_pub_bench <host> <port> <topic> <qos> <count> <size>
 *
 * Publishes <count> messages of <size> bytes at <qos> to <topic>, then disconnects. Exits 0 on
 * success; non-zero on any failure so the harness records that cell as n/a.
 *
 * Build:  gcc -O2 -o paho_pub_bench paho_pub_bench.c -lpaho-mqtt3c
 */

#include <MQTTClient.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

int main(int argc, char** argv)
{
    if (argc != 7)
    {
        fprintf(stderr, "usage: %s host port topic qos count size\n", argv[0]);
        return 2;
    }
    const char* host = argv[1];
    int port = atoi(argv[2]);
    const char* topic = argv[3];
    int qos = atoi(argv[4]);
    long count = atol(argv[5]);
    int size = atoi(argv[6]);

    char uri[256];
    snprintf(uri, sizeof(uri), "tcp://%s:%d", host, port);
    char clientId[64];
    snprintf(clientId, sizeof(clientId), "paho-bench-%ld", (long)getpid());

    MQTTClient client;
    MQTTClient_createOptions createOpts = MQTTClient_createOptions_initializer;
    createOpts.MQTTVersion = MQTTVERSION_5;
    int rc = MQTTClient_createWithOptions(
        &client, uri, clientId, MQTTCLIENT_PERSISTENCE_NONE, NULL, &createOpts);
    if (rc != MQTTCLIENT_SUCCESS)
    {
        fprintf(stderr, "create failed: %d\n", rc);
        return 3;
    }

    MQTTClient_connectOptions conn = MQTTClient_connectOptions_initializer5;
    conn.MQTTVersion = MQTTVERSION_5;
    conn.cleanstart = 1;
    conn.keepAliveInterval = 30;
    /* Allow multiple QoS>0 messages in flight (window-pipelined below) — otherwise paho serialises
     * one ack at a time, and with no TCP_NODELAY knob the per-message round-trip stalls on Nagle /
     * delayed-ACK. The .NET publishers pipeline the same window, keeping it apples-to-apples. */
    conn.reliable = 0;
    conn.maxInflightMessages = 100;

    MQTTResponse cr = MQTTClient_connect5(client, &conn, NULL, NULL);
    if (cr.reasonCode != MQTTREASONCODE_SUCCESS)
    {
        fprintf(stderr, "connect failed: reasonCode=%d\n", cr.reasonCode);
        MQTTResponse_free(cr);
        MQTTClient_destroy(&client);
        return 4;
    }
    MQTTResponse_free(cr);

    char* payload = (char*)malloc(size > 0 ? (size_t)size : 1);
    if (payload == NULL)
    {
        MQTTClient_destroy(&client);
        return 5;
    }
    memset(payload, 'x', (size_t)(size > 0 ? size : 0));

    MQTTClient_message msg = MQTTClient_message_initializer;
    msg.payload = payload;
    msg.payloadlen = size;
    msg.qos = qos;
    msg.retained = 0;

    /* Publish continuously: with reliable=0 + maxInflightMessages, paho keeps a bounded number of
     * QoS>0 messages in flight and throttles publishMessage5 to drain them, so there is no idle gap
     * (which, without a TCP_NODELAY knob, would otherwise stall on Nagle / delayed-ACK). Waiting for
     * the final token confirms the whole stream was delivered. */
    MQTTClient_deliveryToken lastToken = 0;
    int err = 0;
    for (long i = 0; i < count && err == 0; i++)
    {
        MQTTClient_deliveryToken token;
        MQTTResponse pr = MQTTClient_publishMessage5(client, topic, &msg, &token);
        int reason = pr.reasonCode;
        MQTTResponse_free(pr);
        if (reason != MQTTREASONCODE_SUCCESS)
        {
            fprintf(stderr, "publish failed: reasonCode=%d\n", reason);
            err = 6;
            break;
        }
        if (qos > 0)
        {
            lastToken = token;
        }
    }
    if (qos > 0 && err == 0)
    {
        if (MQTTClient_waitForCompletion(client, lastToken, 30000L) != MQTTCLIENT_SUCCESS)
        {
            fprintf(stderr, "waitForCompletion failed\n");
            err = 7;
        }
    }

    free(payload);
    MQTTClient_disconnect5(client, 2000, MQTTREASONCODE_SUCCESS, NULL);
    MQTTClient_destroy(&client);
    return err;
}
