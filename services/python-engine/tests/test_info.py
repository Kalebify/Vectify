def test_info_returns_service_version_and_capabilities(client):
    response = client.get("/api/v1/info")

    assert response.status_code == 200
    body = response.json()
    assert body["service"]
    assert body["version"]
    assert isinstance(body["capabilities"], list)
    assert "health-check" in body["capabilities"]
